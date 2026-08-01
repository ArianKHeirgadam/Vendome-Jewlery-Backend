using GoldInvoice.Application.Catalog;
using GoldInvoice.Application.Common;
using GoldInvoice.Application.Customers;
using GoldInvoice.Application.Inventory;
using GoldInvoice.Application.Invoicing;
using GoldInvoice.Application.Orders;
using GoldInvoice.Application.Payments;
using GoldInvoice.Application.Pricing;
using GoldInvoice.Application.Settings;
using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Inventory;
using GoldInvoice.Domain.Orders;
using GoldInvoice.Domain.Payments;
using GoldInvoice.Domain.Pricing;
using GoldInvoice.Infrastructure.Catalog;
using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Infrastructure.Customers;
using GoldInvoice.Infrastructure.Identity;
using GoldInvoice.Infrastructure.Inventory;
using GoldInvoice.Infrastructure.Invoicing;
using GoldInvoice.Infrastructure.Orders;
using GoldInvoice.Infrastructure.Payments;
using GoldInvoice.Infrastructure.Persistence;
using GoldInvoice.Infrastructure.Persistence.Interceptors;
using GoldInvoice.Infrastructure.Pricing;
using GoldInvoice.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoldInvoice.IntegrationTests;

public sealed class PhaseFiveWorkflowTests
{
    private static readonly DateTimeOffset FixedNow =
        DateTimeOffset.Parse("2026-08-01T12:00:00+00:00");

    [Fact]
    public async Task ManualPayment_AtomicallyConfirmsStockAndIssuesOneImmutableInvoice()
    {
        await using var scenario = await CreateScenarioAsync();
        var paymentService = scenario.CreatePaymentService([]);
        var command = new RecordManualPaymentCommand(
            scenario.Customer.Id,
            scenario.Order.Id,
            PaymentMethod.BankTransfer,
            "BANK-REF-100",
            "manual-payment-key-100");

        var first = await paymentService.RecordManualPaymentAsync(command, CancellationToken.None);
        var duplicate = await paymentService.RecordManualPaymentAsync(command, CancellationToken.None);

        Assert.Equal(first.Id, duplicate.Id);
        Assert.Equal(PaymentStatus.Verified, first.Status);
        Assert.NotNull(first.InvoiceId);
        Assert.Single(await scenario.Context.Payments.ToListAsync());
        Assert.Single(await scenario.Context.Invoices.ToListAsync());
        Assert.Single(await scenario.Context.InvoiceItems.ToListAsync());
        Assert.Single(await scenario.Context.InvoiceAddressSnapshots.ToListAsync());
        Assert.Single(await scenario.Context.InvoiceStoreSnapshots.ToListAsync());
        Assert.Equal(
            StockReservationStatus.Confirmed,
            (await scenario.Context.StockReservations.SingleAsync()).Status);
        Assert.Equal(0, (await scenario.Context.InventoryItems.SingleAsync()).QuantityOnHand);
        Assert.Equal(OrderStatus.Paid, (await scenario.Context.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task DuplicateGatewayCallback_CannotConfirmInventoryOrIssueInvoiceTwice()
    {
        await using var scenario = await CreateScenarioAsync();
        var provider = new FakePaymentGatewayProvider();
        var paymentService = scenario.CreatePaymentService([provider]);
        await paymentService.CreateGatewayAsync(
            new CreatePaymentGatewayCommand(
                "FAKE-PRIMARY",
                "Fake primary gateway",
                provider.ProviderCode,
                "PaymentProviders:FakePrimary"),
            CancellationToken.None);
        var initiated = await paymentService.InitiateAsync(
            new InitiatePaymentCommand(
                scenario.Customer.Id,
                CanManagePayments: false,
                scenario.Order.Id,
                "FAKE-PRIMARY",
                "online-payment-key-100"),
            CancellationToken.None);

        var first = await paymentService.ProcessCallbackAsync(
            provider.ProviderCode,
            "callback-payload-100",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CancellationToken.None);
        var duplicate = await paymentService.ProcessCallbackAsync(
            provider.ProviderCode,
            "callback-payload-100",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CancellationToken.None);

        Assert.Equal(initiated.Payment.Id, first.PaymentId);
        Assert.False(first.IsDuplicate);
        Assert.True(duplicate.IsDuplicate);
        Assert.Equal(first.CallbackId, duplicate.CallbackId);
        Assert.Single(await scenario.Context.PaymentCallbacks.ToListAsync());
        Assert.Single(await scenario.Context.Invoices.ToListAsync());
        Assert.Single(await scenario.Context.InvoiceSequences.ToListAsync());
        Assert.Equal(2, (await scenario.Context.InvoiceSequences.SingleAsync()).NextValue);
        Assert.Single(await scenario.Context.StockMovements
            .Where(movement => movement.MovementType == StockMovementType.ReservationConfirmed)
            .ToListAsync());
    }

    [Fact]
    public async Task AuthenticatedDeclineWithoutAmount_FailsPaymentWithoutConsumingInventory()
    {
        await using var scenario = await CreateScenarioAsync();
        var provider = new FakePaymentGatewayProvider
        {
            IsSuccessful = false,
            IncludeAmount = false
        };
        var paymentService = scenario.CreatePaymentService([provider]);
        await paymentService.CreateGatewayAsync(
            new CreatePaymentGatewayCommand(
                "FAKE-DECLINE",
                "Fake declining gateway",
                provider.ProviderCode,
                "PaymentProviders:FakeDecline"),
            CancellationToken.None);
        var initiated = await paymentService.InitiateAsync(
            new InitiatePaymentCommand(
                scenario.Customer.Id,
                CanManagePayments: false,
                scenario.Order.Id,
                "FAKE-DECLINE",
                "online-payment-key-decline"),
            CancellationToken.None);

        var callback = await paymentService.ProcessCallbackAsync(
            provider.ProviderCode,
            "callback-payload-decline",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CancellationToken.None);

        Assert.Equal("PAYMENT_FAILED", callback.ProcessingResult);
        Assert.Equal(
            PaymentStatus.Failed,
            (await scenario.Context.Payments.SingleAsync(payment => payment.Id == initiated.Payment.Id)).Status);
        Assert.Equal(OrderStatus.AwaitingPayment, (await scenario.Context.Orders.SingleAsync()).Status);
        Assert.Equal(
            StockReservationStatus.Active,
            (await scenario.Context.StockReservations.SingleAsync()).Status);
        Assert.Empty(await scenario.Context.Invoices.ToListAsync());
    }

    [Fact]
    public async Task CallbackVerification_ContinuesPastAFailingGatewayConfiguration()
    {
        await using var scenario = await CreateScenarioAsync();
        var provider = new FakePaymentGatewayProvider
        {
            ThrowingConfigurationReference = "PaymentProviders:Broken"
        };
        var paymentService = scenario.CreatePaymentService([provider]);
        await paymentService.CreateGatewayAsync(
            new CreatePaymentGatewayCommand(
                "A-BROKEN",
                "Broken callback configuration",
                provider.ProviderCode,
                provider.ThrowingConfigurationReference!),
            CancellationToken.None);
        await paymentService.CreateGatewayAsync(
            new CreatePaymentGatewayCommand(
                "B-WORKING",
                "Working callback configuration",
                provider.ProviderCode,
                "PaymentProviders:Working"),
            CancellationToken.None);
        var initiated = await paymentService.InitiateAsync(
            new InitiatePaymentCommand(
                scenario.Customer.Id,
                CanManagePayments: false,
                scenario.Order.Id,
                "B-WORKING",
                "online-multi-config-key"),
            CancellationToken.None);

        var callback = await paymentService.ProcessCallbackAsync(
            provider.ProviderCode,
            "callback-multi-config-payload",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CancellationToken.None);

        Assert.Equal(initiated.Payment.Id, callback.PaymentId);
        Assert.Equal("PAYMENT_VERIFIED", callback.ProcessingResult);
        Assert.Single(await scenario.Context.Invoices.ToListAsync());
    }

    [Fact]
    public async Task SuccessfulCallbackWithoutAuthority_EntersReviewWithoutConsumingInventory()
    {
        await using var scenario = await CreateScenarioAsync();
        var provider = new FakePaymentGatewayProvider { IncludeAuthority = false };
        var paymentService = scenario.CreatePaymentService([provider]);
        await paymentService.CreateGatewayAsync(
            new CreatePaymentGatewayCommand(
                "FAKE-NO-AUTHORITY",
                "Fake gateway without callback authority",
                provider.ProviderCode,
                "PaymentProviders:NoAuthority"),
            CancellationToken.None);
        var initiated = await paymentService.InitiateAsync(
            new InitiatePaymentCommand(
                scenario.Customer.Id,
                CanManagePayments: false,
                scenario.Order.Id,
                "FAKE-NO-AUTHORITY",
                "online-no-authority-key"),
            CancellationToken.None);

        var callback = await paymentService.ProcessCallbackAsync(
            provider.ProviderCode,
            "callback-no-authority-payload",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CancellationToken.None);

        Assert.Equal("CALLBACK_AUTHORITY_MISMATCH", callback.ProcessingResult);
        Assert.Equal(
            PaymentStatus.RequiresReview,
            (await scenario.Context.Payments.SingleAsync(payment => payment.Id == initiated.Payment.Id)).Status);
        Assert.Equal(OrderStatus.PaymentReview, (await scenario.Context.Orders.SingleAsync()).Status);
        Assert.Equal(
            StockReservationStatus.Active,
            (await scenario.Context.StockReservations.SingleAsync()).Status);
        Assert.Empty(await scenario.Context.Invoices.ToListAsync());
    }

    [Fact]
    public async Task ReplayedInitiation_ResumesAPendingAttemptWithTheSamePaymentId()
    {
        await using var scenario = await CreateScenarioAsync();
        var provider = new FakePaymentGatewayProvider();
        var paymentService = scenario.CreatePaymentService([provider]);
        var gateway = await paymentService.CreateGatewayAsync(
            new CreatePaymentGatewayCommand(
                "FAKE-RESUME",
                "Fake resumable gateway",
                provider.ProviderCode,
                "PaymentProviders:FakeResume"),
            CancellationToken.None);
        const string idempotencyKey = "online-payment-resume";
        var payment = new Payment(
            scenario.Order.Id,
            provider.ProviderCode,
            scenario.Order.GrandTotalRials,
            PaymentMethod.OnlineGateway,
            gateway.Id,
            PersistenceUtilities.Hash(
                $"Payments.Online:{scenario.Customer.Id:N}:{idempotencyKey}"));
        var attempt = new PaymentAttempt(payment.Id, 1, payment.AmountRials, FixedNow);
        scenario.Context.AddRange(payment, attempt);
        await scenario.Context.SaveChangesAsync();

        var resumed = await paymentService.InitiateAsync(
            new InitiatePaymentCommand(
                scenario.Customer.Id,
                CanManagePayments: false,
                scenario.Order.Id,
                gateway.Code,
                idempotencyKey),
            CancellationToken.None);

        Assert.Equal(payment.Id, resumed.Payment.Id);
        Assert.Equal(PaymentStatus.Processing, resumed.Payment.Status);
        Assert.StartsWith("https://payments.example.test/", resumed.RedirectUrl, StringComparison.Ordinal);
        Assert.Single(await scenario.Context.Payments.ToListAsync());
        Assert.Single(await scenario.Context.PaymentAttempts.ToListAsync());
    }

    [Fact]
    public async Task CreateOrder_WithTheSameIdempotencyKeyReturnsTheOriginalOrder()
    {
        await using var scenario = await CreateScenarioAsync(createOrder: false);
        var command = scenario.CreateOrderCommand("order-idempotency-key-200");

        var first = await scenario.OrderService.CreateOrderAsync(command, CancellationToken.None);
        var duplicate = await scenario.OrderService.CreateOrderAsync(command, CancellationToken.None);

        Assert.Equal(first.Id, duplicate.Id);
        Assert.Single(await scenario.Context.Orders.ToListAsync());
        Assert.Single(await scenario.Context.OrderItems.ToListAsync());
        Assert.Single(await scenario.Context.StockReservations.ToListAsync());
        Assert.Single(await scenario.Context.IdempotencyRecords.ToListAsync());
    }

    [Fact]
    public async Task CreateOrder_AllowsDistinctPhysicalUnitsFromTheSameInventoryItem()
    {
        await using var scenario = await CreateScenarioAsync(createOrder: false);
        var product = await scenario.Context.Products.SingleAsync();
        var variant = await scenario.Context.ProductVariants.SingleAsync();
        var warehouse = await scenario.Context.Warehouses.SingleAsync();
        var inventory = new InventoryService(scenario.Context, scenario.TimeProvider);
        var firstUnit = await inventory.ReceiveInventoryUnitAsync(
            new ReceiveInventoryUnitCommand(
                product.Id,
                variant.Id,
                warehouse.Id,
                "SERIAL-PHASE5-1",
                "BARCODE-PHASE5-1",
                2m,
                2m,
                18,
                8_000_000,
                FixedNow),
            CancellationToken.None);
        var secondUnit = await inventory.ReceiveInventoryUnitAsync(
            new ReceiveInventoryUnitCommand(
                product.Id,
                variant.Id,
                warehouse.Id,
                "SERIAL-PHASE5-2",
                "BARCODE-PHASE5-2",
                2m,
                2m,
                18,
                8_000_000,
                FixedNow),
            CancellationToken.None);
        var inventoryItem = await scenario.Context.InventoryItems.SingleAsync();
        var itemRowVersion = Convert.ToBase64String(inventoryItem.RowVersion);

        var order = await scenario.OrderService.CreateOrderAsync(
            new CreateOrderCommand(
                scenario.Customer.Id,
                scenario.Customer.Id,
                CanManageOrders: false,
                scenario.Address.Id,
                "0012345678",
                [
                    new CreateOrderLineCommand(
                        inventoryItem.Id,
                        firstUnit.Id,
                        1,
                        ActualGrossWeight: null,
                        ActualNetGoldWeight: null,
                        InventoryRowVersion: itemRowVersion,
                        InventoryUnitRowVersion: firstUnit.RowVersion),
                    new CreateOrderLineCommand(
                        inventoryItem.Id,
                        secondUnit.Id,
                        1,
                        ActualGrossWeight: null,
                        ActualNetGoldWeight: null,
                        InventoryRowVersion: itemRowVersion,
                        InventoryUnitRowVersion: secondUnit.RowVersion)
                ],
                ReservationLifetimeMinutes: 15,
                DiscountRials: 0,
                ShippingRials: 0,
                IdempotencyKey: "order-idempotency-units"),
            CancellationToken.None);

        Assert.Equal(2, order.Items.Count);
        var reservations = await scenario.Context.StockReservations.ToListAsync();
        Assert.Equal(2, reservations.Count);
        Assert.Single(reservations.Select(reservation => reservation.InventoryItemId).Distinct());
        Assert.Equal(2, reservations.Select(reservation => reservation.InventoryUnitId).Distinct().Count());
    }

    [Fact]
    public async Task OrderReservation_CannotBypassPaymentThroughGenericInventoryConfirmation()
    {
        await using var scenario = await CreateScenarioAsync();
        var reservation = await scenario.Context.StockReservations.SingleAsync();
        var inventoryItem = await scenario.Context.InventoryItems.SingleAsync();
        var inventory = new InventoryService(scenario.Context, scenario.TimeProvider);

        await Assert.ThrowsAsync<ApplicationConflictException>(() =>
            inventory.ConfirmReservationAsync(
                reservation.Id,
                Convert.ToBase64String(reservation.RowVersion),
                Convert.ToBase64String(inventoryItem.RowVersion),
                CancellationToken.None));

        Assert.Equal(
            StockReservationStatus.Active,
            (await scenario.Context.StockReservations.SingleAsync()).Status);
        Assert.Equal(OrderStatus.AwaitingPayment, (await scenario.Context.Orders.SingleAsync()).Status);
        Assert.Empty(await scenario.Context.Invoices.ToListAsync());
    }

    [Fact]
    public async Task ManualPayment_CannotSilentlyReplaceAnOnlinePaymentUnderReview()
    {
        await using var scenario = await CreateScenarioAsync();
        var order = await scenario.Context.Orders.SingleAsync();
        order.MarkPaymentReview();
        var gateway = new PaymentGateway(
            "REVIEW-GATEWAY",
            "Review gateway",
            "FAKE-PAYMENT",
            "PaymentProviders:Review");
        var payment = new Payment(
            order.Id,
            gateway.ProviderCode,
            order.GrandTotalRials,
            PaymentMethod.OnlineGateway,
            gateway.Id,
            new string('B', 64));
        payment.RequireReview("CALLBACK_AMOUNT_MISMATCH");
        scenario.Context.AddRange(gateway, payment);
        await scenario.Context.SaveChangesAsync();
        var paymentService = scenario.CreatePaymentService([]);

        await Assert.ThrowsAsync<ApplicationConflictException>(() =>
            paymentService.RecordManualPaymentAsync(
                new RecordManualPaymentCommand(
                    scenario.Customer.Id,
                    order.Id,
                    PaymentMethod.BankTransfer,
                    "MANUAL-REVIEW-BYPASS",
                    "manual-review-bypass-key"),
                CancellationToken.None));

        Assert.Equal(
            PaymentStatus.RequiresReview,
            (await scenario.Context.Payments.SingleAsync()).Status);
        Assert.Equal(OrderStatus.PaymentReview, (await scenario.Context.Orders.SingleAsync()).Status);
        Assert.Empty(await scenario.Context.Invoices.ToListAsync());
    }

    [Fact]
    public async Task PaymentRejectsAReservationThatNoLongerMatchesItsOrderLine()
    {
        await using var scenario = await CreateScenarioAsync();
        var reservation = await scenario.Context.StockReservations.SingleAsync();
        scenario.Context.Entry(reservation).Property(item => item.Quantity).CurrentValue = 2;
        await scenario.Context.SaveChangesAsync();
        var paymentService = scenario.CreatePaymentService([]);

        await Assert.ThrowsAsync<ApplicationConflictException>(() =>
            paymentService.RecordManualPaymentAsync(
                new RecordManualPaymentCommand(
                    scenario.Customer.Id,
                    scenario.Order.Id,
                    PaymentMethod.BankTransfer,
                    "MISMATCHED-RESERVATION",
                    "manual-mismatched-reservation"),
                CancellationToken.None));

        Assert.Empty(await scenario.Context.Payments.ToListAsync());
        Assert.Empty(await scenario.Context.Invoices.ToListAsync());
        Assert.Equal(OrderStatus.AwaitingPayment, (await scenario.Context.Orders.SingleAsync()).Status);
    }

    private static async Task<Scenario> CreateScenarioAsync(bool createOrder = true)
    {
        var timeProvider = new FixedTimeProvider(FixedNow);
        var context = CreateContext(timeProvider);
        var customer = new ApplicationUser("Arian Customer")
        {
            UserName = "customer@example.test",
            NormalizedUserName = "CUSTOMER@EXAMPLE.TEST",
            Email = "customer@example.test",
            NormalizedEmail = "CUSTOMER@EXAMPLE.TEST",
            EmailConfirmed = true
        };
        context.Users.Add(customer);
        await context.SaveChangesAsync();

        var storeService = new StoreProfileService(context);
        await storeService.UpsertAsync(
            new UpdateStoreProfileCommand(
                "Vendome Jewelry",
                "Vendome Jewelry LLC",
                "1010101010",
                "411111111111",
                "10001",
                "041-00000000",
                "5130000000",
                "Tabriz, main jewelry market",
                RowVersion: null),
            CancellationToken.None);
        var addressService = new CustomerAddressService(context);
        var address = await addressService.CreateAddressAsync(
            new CreateCustomerAddressCommand(
                customer.Id,
                customer.Id,
                CanManageCustomer: false,
                "Home",
                "Arian Customer",
                "09120000000",
                "East Azerbaijan",
                "Tabriz",
                "5130000000",
                "Main street",
                IsDefault: true),
            CancellationToken.None);

        var catalog = new CatalogService(context);
        var category = await catalog.CreateCategoryAsync(
            new CreateProductCategoryCommand("Rings", $"rings-{Guid.NewGuid():N}", null, 0),
            CancellationToken.None);
        var product = await catalog.CreateProductAsync(
            new CreateProductCommand(
                category.Id,
                "Classic ring",
                $"classic-ring-{Guid.NewGuid():N}",
                null),
            CancellationToken.None);
        var variant = await catalog.CreateVariantAsync(
            product.Id,
            new CreateProductVariantCommand(
                $"RING-{Guid.NewGuid():N}",
                "Size 52",
                new GoldProductDetailCommand(
                    18,
                    2m,
                    2m,
                    0,
                    0,
                    ManufacturingWageType.FixedRials,
                    0,
                    0,
                    0,
                    HasStone: false,
                    IsWeightVariable: false)),
            CancellationToken.None);
        var marketOptions = Options.Create(new MarketPriceOptions
        {
            ProviderTimeoutSeconds = 2,
            RetryCount = 1,
            RetryBaseDelayMilliseconds = 10,
            MaximumQuoteAgeMinutes = 30,
            MaximumFutureClockSkewSeconds = 30,
            PollIntervalMinutes = 5
        });
        var pricing = new ProductPricingService(
            context,
            new GoldInvoice.Application.Pricing.ProductPriceCalculator(),
            marketOptions,
            timeProvider);
        await pricing.CreateRuleAsync(
            new CreateProductPricingRuleCommand(
                variant.Id,
                PricingMethod.FixedPrice,
                GoldMarketPriceType: null,
                FixedPriceRials: 12_000_000,
                FixedGoldPricePerGramRials: null,
                ManufacturingWageType.FixedRials,
                WageValue: 0,
                ProfitPercentage: 0,
                TaxPercentage: 0,
                FixedNow.AddMinutes(-1),
                EffectiveTo: null),
            CancellationToken.None);
        var warehouse = new Warehouse("MAIN", "Main warehouse");
        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync();
        var inventory = new InventoryService(context, timeProvider);
        var inventoryItem = await inventory.ReceiveStockAsync(
            new ReceiveStockCommand(
                warehouse.Id,
                variant.Id,
                1,
                "Purchase",
                Guid.NewGuid(),
                null),
            CancellationToken.None);
        var coordinator = new InventoryReservationCoordinator(context, timeProvider);
        var orderService = new OrderService(
            context,
            pricing,
            storeService,
            coordinator,
            timeProvider);
        var invoiceService = new InvoiceService(
            context,
            Options.Create(new InvoicingOptions()),
            timeProvider);
        var scenario = new Scenario(
            context,
            customer,
            address,
            inventoryItem,
            orderService,
            coordinator,
            invoiceService,
            timeProvider);
        if (createOrder)
        {
            scenario.Order = await orderService.CreateOrderAsync(
                scenario.CreateOrderCommand("order-idempotency-key-100"),
                CancellationToken.None);
        }

        return scenario;
    }

    private static GoldInvoiceDbContext CreateContext(TimeProvider timeProvider)
    {
        var options = new DbContextOptionsBuilder<GoldInvoiceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .AddInterceptors(new AuditingSaveChangesInterceptor(timeProvider))
            .Options;
        return new GoldInvoiceDbContext(options);
    }

    private sealed class Scenario(
        GoldInvoiceDbContext context,
        ApplicationUser customer,
        CustomerAddressInfo address,
        InventoryItemInfo inventoryItem,
        OrderService orderService,
        InventoryReservationCoordinator coordinator,
        InvoiceService invoiceService,
        TimeProvider timeProvider) : IAsyncDisposable
    {
        public GoldInvoiceDbContext Context { get; } = context;
        public ApplicationUser Customer { get; } = customer;
        public CustomerAddressInfo Address { get; } = address;
        public OrderInfo Order { get; set; } = null!;
        public OrderService OrderService { get; } = orderService;
        public TimeProvider TimeProvider { get; } = timeProvider;

        public CreateOrderCommand CreateOrderCommand(string idempotencyKey) => new(
            customer.Id,
            customer.Id,
            CanManageOrders: false,
            address.Id,
            "0012345678",
            [new CreateOrderLineCommand(
                inventoryItem.Id,
                InventoryUnitId: null,
                Quantity: 1,
                ActualGrossWeight: null,
                ActualNetGoldWeight: null,
                inventoryItem.RowVersion,
                InventoryUnitRowVersion: null)],
            ReservationLifetimeMinutes: 15,
            DiscountRials: 0,
            ShippingRials: 0,
            idempotencyKey);

        public PaymentService CreatePaymentService(IEnumerable<IPaymentGatewayProvider> providers) => new(
            context,
            providers,
            coordinator,
            invoiceService,
            Options.Create(new PaymentProcessingOptions
            {
                ProviderTimeoutSeconds = 2,
                MaximumGatewayConfigurationsPerProvider = 2
            }),
            timeProvider,
            NullLogger<PaymentService>.Instance);

        public ValueTask DisposeAsync() => context.DisposeAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakePaymentGatewayProvider : IPaymentGatewayProvider
    {
        private PaymentGatewayInitiationRequest? initiation;

        public string ProviderCode => "FAKE-PAYMENT";

        public bool IsSuccessful { get; init; } = true;

        public bool IncludeAmount { get; init; } = true;

        public bool IncludeAuthority { get; init; } = true;

        public string? ThrowingConfigurationReference { get; init; }

        public Task<PaymentGatewayInitiationResult> InitiateAsync(
            PaymentGatewayInitiationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            initiation = request;
            return Task.FromResult(new PaymentGatewayInitiationResult(
                $"AUTH-{request.PaymentId:N}",
                $"REQUEST-{request.PaymentId:N}",
                "https://payments.example.test/pay",
                "{\"provider\":\"fake\"}"));
        }

        public Task<PaymentGatewayCallbackResult> VerifyCallbackAsync(
            PaymentGatewayCallbackRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(
                    request.ConfigurationReference,
                    ThrowingConfigurationReference,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Simulated provider configuration failure.");
            }

            var current = initiation ?? throw new InvalidOperationException("Payment was not initiated.");
            if (!string.Equals(
                    request.ConfigurationReference,
                    current.ConfigurationReference,
                    StringComparison.Ordinal))
            {
                return Task.FromResult(new PaymentGatewayCallbackResult(
                    IsAuthentic: false,
                    ExternalCallbackId: null,
                    MerchantPaymentId: null,
                    Authority: null,
                    GatewayPaymentId: null,
                    AmountRials: null,
                    IsSuccessful: false,
                    FailureCode: null,
                    MaskedPayloadJson: null));
            }

            return Task.FromResult(new PaymentGatewayCallbackResult(
                IsAuthentic: true,
                ExternalCallbackId: $"CALLBACK-{current.PaymentId:N}",
                MerchantPaymentId: current.PaymentId,
                Authority: IncludeAuthority ? $"AUTH-{current.PaymentId:N}" : null,
                GatewayPaymentId: IsSuccessful ? $"GATEWAY-{current.PaymentId:N}" : null,
                AmountRials: IncludeAmount ? current.AmountRials : null,
                IsSuccessful: IsSuccessful,
                FailureCode: IsSuccessful ? null : "DECLINED",
                MaskedPayloadJson: IsSuccessful
                    ? "{\"status\":\"verified\"}"
                    : "{\"status\":\"declined\"}"));
        }
    }
}
