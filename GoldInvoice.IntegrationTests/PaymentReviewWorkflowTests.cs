using GoldInvoice.Application.Catalog;
using GoldInvoice.Application.Common;
using GoldInvoice.Application.Customers;
using GoldInvoice.Application.Inventory;
using GoldInvoice.Application.Integration;
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
using GoldInvoice.Infrastructure.Integration;
using GoldInvoice.Infrastructure.Orders;
using GoldInvoice.Infrastructure.Payments;
using GoldInvoice.Infrastructure.Persistence;
using GoldInvoice.Infrastructure.Persistence.Interceptors;
using GoldInvoice.Infrastructure.Pricing;
using GoldInvoice.Infrastructure.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoldInvoice.IntegrationTests;

public sealed class PaymentReviewWorkflowTests
{
    private static readonly DateTimeOffset FixedNow =
        DateTimeOffset.Parse("2026-08-01T12:00:00+00:00");

    [Fact]
    public async Task ManagerVerify_WithFundsConfirmed_CompletesTheReviewPayment()
    {
        await using var scenario = await CreateScenarioAsync();
        var provider = new FakePaymentGatewayProvider { IncludeAuthority = false };
        var paymentService = scenario.CreatePaymentService([provider]);
        await paymentService.CreateGatewayAsync(
            new CreatePaymentGatewayCommand(
                "FAKE-REVIEW-VERIFY",
                "Fake review gateway",
                provider.ProviderCode,
                "PaymentProviders:ReviewVerify"),
            CancellationToken.None);
        var initiated = await paymentService.InitiateAsync(
            new InitiatePaymentCommand(
                scenario.Customer.Id,
                CanManagePayments: false,
                scenario.Order.Id,
                "FAKE-REVIEW-VERIFY",
                "review-verify-key"),
            CancellationToken.None);
        await paymentService.ProcessCallbackAsync(
            provider.ProviderCode,
            "callback-review-payload",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CancellationToken.None);
        Assert.Equal(
            PaymentStatus.RequiresReview,
            (await scenario.Context.Payments.SingleAsync()).Status);
        var underReview = await paymentService.GetPaymentAsync(
            initiated.Payment.Id,
            scenario.Customer.Id,
            canReadAll: false,
            CancellationToken.None);

        var resolved = await paymentService.VerifyReviewPaymentAsync(
            new VerifyReviewPaymentCommand(
                scenario.Customer.Id,
                underReview.Id,
                GatewayPaymentId: null,
                underReview.RowVersion),
            CancellationToken.None);

        Assert.Equal(PaymentStatus.Verified, resolved.Status);
        Assert.Equal("MANUAL-" + underReview.Id.ToString("N"), resolved.GatewayPaymentId);
        Assert.Equal(
            OrderStatus.Paid,
            (await scenario.Context.Orders.SingleAsync()).Status);
        Assert.Equal(
            StockReservationStatus.Confirmed,
            (await scenario.Context.StockReservations.SingleAsync()).Status);
        Assert.Single(await scenario.Context.Invoices.ToListAsync());
    }

    [Fact]
    public async Task ManagerReject_ReturnsOrderToAwaitingPaymentAndFreesTheReservation()
    {
        await using var scenario = await CreateScenarioAsync();
        var provider = new FakePaymentGatewayProvider { IncludeAuthority = false };
        var paymentService = scenario.CreatePaymentService([provider]);
        await paymentService.CreateGatewayAsync(
            new CreatePaymentGatewayCommand(
                "FAKE-REVIEW-REJECT",
                "Fake review gateway",
                provider.ProviderCode,
                "PaymentProviders:ReviewReject"),
            CancellationToken.None);
        var initiated = await paymentService.InitiateAsync(
            new InitiatePaymentCommand(
                scenario.Customer.Id,
                CanManagePayments: false,
                scenario.Order.Id,
                "FAKE-REVIEW-REJECT",
                "review-reject-key"),
            CancellationToken.None);
        await paymentService.ProcessCallbackAsync(
            provider.ProviderCode,
            "callback-review-payload",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CancellationToken.None);
        var underReview = await paymentService.GetPaymentAsync(
            initiated.Payment.Id,
            scenario.Customer.Id,
            canReadAll: false,
            CancellationToken.None);

        var rejected = await paymentService.RejectReviewPaymentAsync(
            new RejectReviewPaymentCommand(
                scenario.Customer.Id,
                underReview.Id,
                "Customer will retry with the correct bank account.",
                underReview.RowVersion),
            CancellationToken.None);

        Assert.Equal(PaymentStatus.Failed, rejected.Status);
        Assert.Equal("REVIEW_REJECTED", rejected.FailureCode);
        Assert.Equal(
            OrderStatus.AwaitingPayment,
            (await scenario.Context.Orders.SingleAsync()).Status);
        Assert.Equal(
            StockReservationStatus.Active,
            (await scenario.Context.StockReservations.SingleAsync()).Status);
        Assert.Empty(await scenario.Context.Invoices.ToListAsync());

        var retried = await paymentService.InitiateAsync(
            new InitiatePaymentCommand(
                scenario.Customer.Id,
                CanManagePayments: false,
                scenario.Order.Id,
                "FAKE-REVIEW-REJECT",
                "review-retry-key"),
            CancellationToken.None);
        Assert.Equal(PaymentStatus.Processing, retried.Payment.Status);
        Assert.Equal(
            StockReservationStatus.Active,
            (await scenario.Context.StockReservations.SingleAsync()).Status);
    }

    [Fact]
    public async Task ReviewResolution_WithMalformedRowVersion_IsRejected()
    {
        await using var scenario = await CreateScenarioAsync();
        var provider = new FakePaymentGatewayProvider { IncludeAuthority = false };
        var paymentService = scenario.CreatePaymentService([provider]);
        await paymentService.CreateGatewayAsync(
            new CreatePaymentGatewayCommand(
                "FAKE-REVIEW-BADTOKEN",
                "Fake review gateway",
                provider.ProviderCode,
                "PaymentProviders:ReviewBadToken"),
            CancellationToken.None);
        var initiated = await paymentService.InitiateAsync(
            new InitiatePaymentCommand(
                scenario.Customer.Id,
                CanManagePayments: false,
                scenario.Order.Id,
                "FAKE-REVIEW-BADTOKEN",
                "review-bad-token-key"),
            CancellationToken.None);
        await paymentService.ProcessCallbackAsync(
            provider.ProviderCode,
            "callback-review-payload",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CancellationToken.None);
        var underReview = await paymentService.GetPaymentAsync(
            initiated.Payment.Id,
            scenario.Customer.Id,
            canReadAll: false,
            CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            paymentService.VerifyReviewPaymentAsync(
                new VerifyReviewPaymentCommand(
                    scenario.Customer.Id,
                    underReview.Id,
                    GatewayPaymentId: null,
                    "not-valid-base64"),
                CancellationToken.None));

        Assert.Equal(PaymentStatus.RequiresReview, underReview.Status);
        Assert.Equal(
            PaymentStatus.RequiresReview,
            (await scenario.Context.Payments.SingleAsync()).Status);
        Assert.Equal(
            OrderStatus.PaymentReview,
            (await scenario.Context.Orders.SingleAsync()).Status);
        Assert.Empty(await scenario.Context.Invoices.ToListAsync());
    }

    [Fact]
    public async Task SuccessfulCallback_WithExpiredReservation_ReactivatesInventoryAndCompletes()
    {
        await using var scenario = await CreateScenarioAsync();
        var provider = new FakePaymentGatewayProvider();
        var paymentService = scenario.CreatePaymentService([provider]);
        await paymentService.CreateGatewayAsync(
            new CreatePaymentGatewayCommand(
                "FAKE-REACTIVATE",
                "Fake gateway",
                provider.ProviderCode,
                "PaymentProviders:Reactivate"),
            CancellationToken.None);
        var initiated = await paymentService.InitiateAsync(
            new InitiatePaymentCommand(
                scenario.Customer.Id,
                CanManagePayments: false,
                scenario.Order.Id,
                "FAKE-REACTIVATE",
                "reactivate-key"),
            CancellationToken.None);
        var reservation = await scenario.Context.StockReservations.SingleAsync();
        var item = await scenario.Context.InventoryItems.FindAsync([scenario.inventoryItem.Id]);
        item!.ReleaseReservation(reservation.Quantity);
        reservation.Expire(FixedNow.AddMinutes(20));
        await scenario.Context.SaveChangesAsync();

        var callback = await paymentService.ProcessCallbackAsync(
            provider.ProviderCode,
            "callback-success-payload",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CancellationToken.None);

        Assert.Equal(initiated.Payment.Id, callback.PaymentId);
        Assert.Equal("PAYMENT_VERIFIED", callback.ProcessingResult);
        var resolved = await scenario.Context.StockReservations.SingleAsync();
        Assert.Equal(StockReservationStatus.Confirmed, resolved.Status);
        Assert.Equal(FixedNow.AddMinutes(30), resolved.ExpiresAt);
        Assert.Equal(
            OrderStatus.Paid,
            (await scenario.Context.Orders.SingleAsync()).Status);
        Assert.Equal(
            PaymentStatus.Verified,
            (await scenario.Context.Payments.SingleAsync()).Status);
        Assert.Single(await scenario.Context.Invoices.ToListAsync());
    }

    private static async Task<Scenario> CreateScenarioAsync()
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
        var outboxWriter = new OutboxWriter(context, new HttpContextAccessor());
        var inventory = new InventoryService(context, outboxWriter, timeProvider);
        var inventoryItem = await inventory.ReceiveStockAsync(
            new ReceiveStockCommand(
                warehouse.Id,
                variant.Id,
                1,
                "Purchase",
                Guid.NewGuid(),
                null),
            CancellationToken.None);
        var coordinator = new InventoryReservationCoordinator(context, outboxWriter, timeProvider);
        var orderService = new OrderService(
            context,
            pricing,
            storeService,
            coordinator,
            outboxWriter,
            timeProvider);
        var invoiceService = new InvoiceService(
            context,
            Options.Create(new InvoicingOptions()),
            outboxWriter,
            timeProvider);
        var scenario = new Scenario(
            context,
            customer,
            address,
            inventoryItem,
            orderService,
            coordinator,
            invoiceService,
            outboxWriter,
            timeProvider);
        scenario.Order = await orderService.CreateOrderAsync(
            scenario.CreateOrderCommand("order-idempotency-key-payrev"),
            CancellationToken.None);
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
        IOutboxWriter outboxWriter,
        TimeProvider timeProvider) : IAsyncDisposable
    {
        public GoldInvoiceDbContext Context { get; } = context;
        public ApplicationUser Customer { get; } = customer;
        public CustomerAddressInfo Address { get; } = address;
        public InventoryItemInfo inventoryItem { get; } = inventoryItem;
        public OrderService OrderService { get; } = orderService;
        public InventoryReservationCoordinator coordinator { get; } = coordinator;
        public InvoiceService InvoiceService { get; } = invoiceService;
        public IOutboxWriter outboxWriter { get; } = outboxWriter;
        public TimeProvider TimeProvider { get; } = timeProvider;
        public OrderInfo Order { get; set; } = null!;

        public CreateOrderCommand CreateOrderCommand(string idempotencyKey) => new(
            Customer.Id,
            Customer.Id,
            CanManageOrders: false,
            Address.Id,
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
            Context,
            providers,
            coordinator,
            InvoiceService,
            outboxWriter,
            Options.Create(new PaymentProcessingOptions
            {
                ProviderTimeoutSeconds = 2,
                MaximumGatewayConfigurationsPerProvider = 2
            }),
            TimeProvider,
            NullLogger<PaymentService>.Instance);

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakePaymentGatewayProvider : IPaymentGatewayProvider
    {
        private PaymentGatewayInitiationRequest? initiation;

        public string ProviderCode => "FAKE-PAYMENT";

        public bool IncludeAuthority { get; init; } = true;

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
            var current = initiation ?? throw new InvalidOperationException("Payment was not initiated.");
            return Task.FromResult(new PaymentGatewayCallbackResult(
                IsAuthentic: true,
                ExternalCallbackId: $"CALLBACK-{current.PaymentId:N}",
                MerchantPaymentId: current.PaymentId,
                Authority: IncludeAuthority ? $"AUTH-{current.PaymentId:N}" : null,
                GatewayPaymentId: $"GATEWAY-{current.PaymentId:N}",
                AmountRials: current.AmountRials,
                IsSuccessful: true,
                FailureCode: null,
                MaskedPayloadJson: "{\"status\":\"verified\"}"));
        }
    }
}