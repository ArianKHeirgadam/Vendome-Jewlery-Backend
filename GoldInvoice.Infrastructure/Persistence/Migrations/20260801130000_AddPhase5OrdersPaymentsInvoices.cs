using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoldInvoice.Infrastructure.Persistence.Migrations
{
    public partial class AddPhase5OrdersPaymentsInvoices : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Status",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderStatusHistory_FromStatus",
                schema: "sales",
                table: "OrderStatusHistory");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderStatusHistory_ToStatus",
                schema: "sales",
                table: "OrderStatusHistory");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Status",
                schema: "billing",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_StockReservations_OrderId_InventoryItemId",
                schema: "inventory",
                table: "StockReservations");

            migrationBuilder.AddColumn<Guid>(
                name: "OrderItemId",
                schema: "inventory",
                table: "StockReservations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryUnitId",
                schema: "invoicing",
                table: "InvoiceItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GoldValueRials",
                schema: "invoicing",
                table: "InvoiceItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Karat",
                schema: "invoicing",
                table: "InvoiceItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MarketUnitPriceRials",
                schema: "invoicing",
                table: "InvoiceItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetGoldWeightGrams",
                schema: "invoicing",
                table: "InvoiceItems",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrderItemId",
                schema: "invoicing",
                table: "InvoiceItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PriceCalculationSnapshotId",
                schema: "invoicing",
                table: "InvoiceItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProfitRials",
                schema: "invoicing",
                table: "InvoiceItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoundingPolicy",
                schema: "invoicing",
                table: "InvoiceItems",
                type: "varchar(100)",
                unicode: false,
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TaxRials",
                schema: "invoicing",
                table: "InvoiceItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WageRials",
                schema: "invoicing",
                table: "InvoiceItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentId",
                schema: "invoicing",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerAddressId",
                schema: "sales",
                table: "OrderAddressSnapshots",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GoldValueRials",
                schema: "sales",
                table: "OrderItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryItemId",
                schema: "sales",
                table: "OrderItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryUnitId",
                schema: "sales",
                table: "OrderItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Karat",
                schema: "sales",
                table: "OrderItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MarketUnitPriceRials",
                schema: "sales",
                table: "OrderItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetGoldWeightGrams",
                schema: "sales",
                table: "OrderItems",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PriceCalculationSnapshotId",
                schema: "sales",
                table: "OrderItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProfitRials",
                schema: "sales",
                table: "OrderItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoundingPolicy",
                schema: "sales",
                table: "OrderItems",
                type: "varchar(100)",
                unicode: false,
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TaxRials",
                schema: "sales",
                table: "OrderItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WageRials",
                schema: "sales",
                table: "OrderItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerNameSnapshot",
                schema: "sales",
                table: "Orders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerNationalIdSnapshot",
                schema: "sales",
                table: "Orders",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                schema: "billing",
                table: "Payments",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKeyHash",
                schema: "billing",
                table: "Payments",
                type: "varchar(128)",
                unicode: false,
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Method",
                schema: "billing",
                table: "Payments",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: false,
                defaultValue: "OnlineGateway");

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentGatewayId",
                schema: "billing",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RedirectUrl",
                schema: "billing",
                table: "PaymentAttempts",
                type: "varchar(2000)",
                unicode: false,
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerAddresses",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PhoneNumber = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Province = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PostalCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    AddressLine = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAddresses", x => x.Id);
                    table.CheckConstraint(
                        "CK_CustomerAddresses_SoftDelete",
                        "([IsDeleted] = 0 AND [DeletedAt] IS NULL) OR ([IsDeleted] = 1 AND [DeletedAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CustomerAddresses_Users_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "security",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InvoiceSequences",
                schema: "invoicing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Series = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Prefix = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    NextValue = table.Column<long>(type: "bigint", nullable: false),
                    LastIssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceSequences", x => x.Id);
                    table.CheckConstraint("CK_InvoiceSequences_NextValue", "[NextValue] > 0");
                });

            migrationBuilder.CreateTable(
                name: "OrderStoreSnapshots",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TradeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LegalName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NationalId = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    EconomicCode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    RegistrationNumber = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    PhoneNumber = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    PostalCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    AddressLine = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderStoreSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderStoreSnapshots_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "sales",
                        principalTable: "Orders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PaymentGateways",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProviderCode = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    ConfigurationReference = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentGateways", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceAddressSnapshots",
                schema: "invoicing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderAddressSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PhoneNumber = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Province = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PostalCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    AddressLine = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceAddressSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceAddressSnapshots_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "invoicing",
                        principalTable: "Invoices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvoiceAddressSnapshots_OrderAddressSnapshots_OrderAddressSnapshotId",
                        column: x => x.OrderAddressSnapshotId,
                        principalSchema: "sales",
                        principalTable: "OrderAddressSnapshots",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InvoiceStoreSnapshots",
                schema: "invoicing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderStoreSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TradeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LegalName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NationalId = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    EconomicCode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    RegistrationNumber = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    PhoneNumber = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    PostalCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    AddressLine = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceStoreSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceStoreSnapshots_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "invoicing",
                        principalTable: "Invoices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvoiceStoreSnapshots_OrderStoreSnapshots_OrderStoreSnapshotId",
                        column: x => x.OrderStoreSnapshotId,
                        principalSchema: "sales",
                        principalTable: "OrderStoreSnapshots",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceItems_PriceSnapshot",
                schema: "invoicing",
                table: "InvoiceItems",
                sql: "([OrderItemId] IS NULL AND [PriceCalculationSnapshotId] IS NULL AND [InventoryUnitId] IS NULL AND [NetGoldWeightGrams] IS NULL AND [Karat] IS NULL AND [MarketUnitPriceRials] IS NULL AND [GoldValueRials] IS NULL AND [WageRials] IS NULL AND [ProfitRials] IS NULL AND [TaxRials] IS NULL AND [RoundingPolicy] IS NULL) OR ([OrderItemId] IS NOT NULL AND [PriceCalculationSnapshotId] IS NOT NULL AND [NetGoldWeightGrams] > 0 AND [NetGoldWeightGrams] <= [WeightGrams] AND [Karat] IN (9, 10, 14, 18, 21, 22, 24) AND [MarketUnitPriceRials] >= 0 AND [GoldValueRials] >= 0 AND [WageRials] >= 0 AND [ProfitRials] >= 0 AND [TaxRials] >= 0 AND [UnitPriceRials] = [GoldValueRials] + [WageRials] + [ProfitRials] + [TaxRials] AND [RoundingPolicy] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderItems_PriceSnapshot",
                schema: "sales",
                table: "OrderItems",
                sql: "([PriceCalculationSnapshotId] IS NULL AND [InventoryItemId] IS NULL AND [InventoryUnitId] IS NULL AND [NetGoldWeightGrams] IS NULL AND [Karat] IS NULL AND [MarketUnitPriceRials] IS NULL AND [GoldValueRials] IS NULL AND [WageRials] IS NULL AND [ProfitRials] IS NULL AND [TaxRials] IS NULL AND [RoundingPolicy] IS NULL) OR ([PriceCalculationSnapshotId] IS NOT NULL AND [InventoryItemId] IS NOT NULL AND [NetGoldWeightGrams] > 0 AND [NetGoldWeightGrams] <= [WeightGrams] AND [Karat] IN (9, 10, 14, 18, 21, 22, 24) AND [MarketUnitPriceRials] >= 0 AND [GoldValueRials] >= 0 AND [WageRials] >= 0 AND [ProfitRials] >= 0 AND [TaxRials] >= 0 AND [UnitPriceRials] = [GoldValueRials] + [WageRials] + [ProfitRials] + [TaxRials] AND [RoundingPolicy] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Status",
                schema: "sales",
                table: "Orders",
                sql: "[Status] IN ('Pending', 'AwaitingPayment', 'PaymentReview', 'Paid', 'Processing', 'Completed', 'Cancelled', 'Refunded')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderStatusHistory_FromStatus",
                schema: "sales",
                table: "OrderStatusHistory",
                sql: "[FromStatus] IS NULL OR [FromStatus] IN ('Pending', 'AwaitingPayment', 'PaymentReview', 'Paid', 'Processing', 'Completed', 'Cancelled', 'Refunded')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderStatusHistory_ToStatus",
                schema: "sales",
                table: "OrderStatusHistory",
                sql: "[ToStatus] IN ('Pending', 'AwaitingPayment', 'PaymentReview', 'Paid', 'Processing', 'Completed', 'Cancelled', 'Refunded')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Method",
                schema: "billing",
                table: "Payments",
                sql: "[Method] IN ('OnlineGateway', 'Cash', 'PointOfSale', 'BankTransfer', 'CardToCard')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Status",
                schema: "billing",
                table: "Payments",
                sql: "[Status] IN ('Pending', 'Processing', 'Verified', 'RequiresReview', 'Failed', 'Cancelled', 'Refunded')");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_OrderItemId",
                schema: "inventory",
                table: "StockReservations",
                column: "OrderItemId",
                unique: true,
                filter: "[OrderItemId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_OrderId_InventoryItemId",
                schema: "inventory",
                table: "StockReservations",
                columns: new[] { "OrderId", "InventoryItemId" },
                unique: true,
                filter: "[Status] = 'Active' AND [InventoryUnitId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_InventoryUnitId",
                schema: "invoicing",
                table: "InvoiceItems",
                column: "InventoryUnitId",
                unique: true,
                filter: "[InventoryUnitId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_OrderItemId",
                schema: "invoicing",
                table: "InvoiceItems",
                column: "OrderItemId",
                unique: true,
                filter: "[OrderItemId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_PriceCalculationSnapshotId",
                schema: "invoicing",
                table: "InvoiceItems",
                column: "PriceCalculationSnapshotId",
                unique: true,
                filter: "[PriceCalculationSnapshotId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PaymentId",
                schema: "invoicing",
                table: "Invoices",
                column: "PaymentId",
                unique: true,
                filter: "[PaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrderAddressSnapshots_CustomerAddressId",
                schema: "sales",
                table: "OrderAddressSnapshots",
                column: "CustomerAddressId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_InventoryItemId",
                schema: "sales",
                table: "OrderItems",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_InventoryUnitId",
                schema: "sales",
                table: "OrderItems",
                column: "InventoryUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_PriceCalculationSnapshotId",
                schema: "sales",
                table: "OrderItems",
                column: "PriceCalculationSnapshotId",
                unique: true,
                filter: "[PriceCalculationSnapshotId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_IdempotencyKeyHash",
                schema: "billing",
                table: "Payments",
                column: "IdempotencyKeyHash",
                unique: true,
                filter: "[IdempotencyKeyHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderId",
                schema: "billing",
                table: "Payments",
                column: "OrderId",
                unique: true,
                filter: "[Status] IN ('Pending', 'Processing', 'RequiresReview')");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PaymentGatewayId",
                schema: "billing",
                table: "Payments",
                column: "PaymentGatewayId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAddresses_CustomerId",
                schema: "sales",
                table: "CustomerAddresses",
                column: "CustomerId",
                unique: true,
                filter: "[IsDefault] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAddresses_CustomerId_CreatedAt",
                schema: "sales",
                table: "CustomerAddresses",
                columns: new[] { "CustomerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceAddressSnapshots_InvoiceId",
                schema: "invoicing",
                table: "InvoiceAddressSnapshots",
                column: "InvoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceAddressSnapshots_OrderAddressSnapshotId",
                schema: "invoicing",
                table: "InvoiceAddressSnapshots",
                column: "OrderAddressSnapshotId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceSequences_Prefix",
                schema: "invoicing",
                table: "InvoiceSequences",
                column: "Prefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceSequences_Series",
                schema: "invoicing",
                table: "InvoiceSequences",
                column: "Series",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceStoreSnapshots_InvoiceId",
                schema: "invoicing",
                table: "InvoiceStoreSnapshots",
                column: "InvoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceStoreSnapshots_OrderStoreSnapshotId",
                schema: "invoicing",
                table: "InvoiceStoreSnapshots",
                column: "OrderStoreSnapshotId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderStoreSnapshots_OrderId",
                schema: "sales",
                table: "OrderStoreSnapshots",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentGateways_Code",
                schema: "billing",
                table: "PaymentGateways",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentGateways_ProviderCode_IsActive",
                schema: "billing",
                table: "PaymentGateways",
                columns: new[] { "ProviderCode", "IsActive" });

            migrationBuilder.AddForeignKey(
                name: "FK_StockReservations_OrderItems_OrderItemId",
                schema: "inventory",
                table: "StockReservations",
                column: "OrderItemId",
                principalSchema: "sales",
                principalTable: "OrderItems",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceItems_InventoryUnits_InventoryUnitId",
                schema: "invoicing",
                table: "InvoiceItems",
                column: "InventoryUnitId",
                principalSchema: "inventory",
                principalTable: "InventoryUnits",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceItems_OrderItems_OrderItemId",
                schema: "invoicing",
                table: "InvoiceItems",
                column: "OrderItemId",
                principalSchema: "sales",
                principalTable: "OrderItems",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceItems_PriceCalculationSnapshots_PriceCalculationSnapshotId",
                schema: "invoicing",
                table: "InvoiceItems",
                column: "PriceCalculationSnapshotId",
                principalSchema: "pricing",
                principalTable: "PriceCalculationSnapshots",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Payments_PaymentId",
                schema: "invoicing",
                table: "Invoices",
                column: "PaymentId",
                principalSchema: "billing",
                principalTable: "Payments",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderAddressSnapshots_CustomerAddresses_CustomerAddressId",
                schema: "sales",
                table: "OrderAddressSnapshots",
                column: "CustomerAddressId",
                principalSchema: "sales",
                principalTable: "CustomerAddresses",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_InventoryItems_InventoryItemId",
                schema: "sales",
                table: "OrderItems",
                column: "InventoryItemId",
                principalSchema: "inventory",
                principalTable: "InventoryItems",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_InventoryUnits_InventoryUnitId",
                schema: "sales",
                table: "OrderItems",
                column: "InventoryUnitId",
                principalSchema: "inventory",
                principalTable: "InventoryUnits",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_PriceCalculationSnapshots_PriceCalculationSnapshotId",
                schema: "sales",
                table: "OrderItems",
                column: "PriceCalculationSnapshotId",
                principalSchema: "pricing",
                principalTable: "PriceCalculationSnapshots",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_PaymentGateways_PaymentGatewayId",
                schema: "billing",
                table: "Payments",
                column: "PaymentGatewayId",
                principalSchema: "billing",
                principalTable: "PaymentGateways",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockReservations_OrderItems_OrderItemId",
                schema: "inventory",
                table: "StockReservations");
            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceItems_InventoryUnits_InventoryUnitId",
                schema: "invoicing",
                table: "InvoiceItems");
            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceItems_OrderItems_OrderItemId",
                schema: "invoicing",
                table: "InvoiceItems");
            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceItems_PriceCalculationSnapshots_PriceCalculationSnapshotId",
                schema: "invoicing",
                table: "InvoiceItems");
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Payments_PaymentId",
                schema: "invoicing",
                table: "Invoices");
            migrationBuilder.DropForeignKey(
                name: "FK_OrderAddressSnapshots_CustomerAddresses_CustomerAddressId",
                schema: "sales",
                table: "OrderAddressSnapshots");
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_InventoryItems_InventoryItemId",
                schema: "sales",
                table: "OrderItems");
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_InventoryUnits_InventoryUnitId",
                schema: "sales",
                table: "OrderItems");
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_PriceCalculationSnapshots_PriceCalculationSnapshotId",
                schema: "sales",
                table: "OrderItems");
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_PaymentGateways_PaymentGatewayId",
                schema: "billing",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceItems_PriceSnapshot",
                schema: "invoicing",
                table: "InvoiceItems");
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderItems_PriceSnapshot",
                schema: "sales",
                table: "OrderItems");
            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Status",
                schema: "sales",
                table: "Orders");
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderStatusHistory_FromStatus",
                schema: "sales",
                table: "OrderStatusHistory");
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderStatusHistory_ToStatus",
                schema: "sales",
                table: "OrderStatusHistory");
            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Method",
                schema: "billing",
                table: "Payments");
            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Status",
                schema: "billing",
                table: "Payments");

            migrationBuilder.Sql(
                "UPDATE [sales].[Orders] SET [Status] = 'AwaitingPayment' WHERE [Status] = 'PaymentReview'; " +
                "UPDATE [sales].[OrderStatusHistory] SET [FromStatus] = 'AwaitingPayment' WHERE [FromStatus] = 'PaymentReview'; " +
                "UPDATE [sales].[OrderStatusHistory] SET [ToStatus] = 'AwaitingPayment' WHERE [ToStatus] = 'PaymentReview'; " +
                "UPDATE [billing].[Payments] SET [Status] = 'Failed', [FailureCode] = COALESCE([FailureCode], 'REVIEW_ROLLBACK'), [FailedAt] = COALESCE([FailedAt], [UpdatedAt]) WHERE [Status] = 'RequiresReview';");

            migrationBuilder.DropTable(name: "InvoiceAddressSnapshots", schema: "invoicing");
            migrationBuilder.DropTable(name: "InvoiceStoreSnapshots", schema: "invoicing");
            migrationBuilder.DropTable(name: "InvoiceSequences", schema: "invoicing");
            migrationBuilder.DropTable(name: "OrderStoreSnapshots", schema: "sales");
            migrationBuilder.DropTable(name: "CustomerAddresses", schema: "sales");
            migrationBuilder.DropTable(name: "PaymentGateways", schema: "billing");

            migrationBuilder.DropIndex(name: "IX_StockReservations_OrderItemId", schema: "inventory", table: "StockReservations");

            migrationBuilder.DropIndex(name: "IX_StockReservations_OrderId_InventoryItemId", schema: "inventory", table: "StockReservations");
            migrationBuilder.DropIndex(name: "IX_InvoiceItems_InventoryUnitId", schema: "invoicing", table: "InvoiceItems");
            migrationBuilder.DropIndex(name: "IX_InvoiceItems_OrderItemId", schema: "invoicing", table: "InvoiceItems");
            migrationBuilder.DropIndex(name: "IX_InvoiceItems_PriceCalculationSnapshotId", schema: "invoicing", table: "InvoiceItems");
            migrationBuilder.DropIndex(name: "IX_Invoices_PaymentId", schema: "invoicing", table: "Invoices");
            migrationBuilder.DropIndex(name: "IX_OrderAddressSnapshots_CustomerAddressId", schema: "sales", table: "OrderAddressSnapshots");
            migrationBuilder.DropIndex(name: "IX_OrderItems_InventoryItemId", schema: "sales", table: "OrderItems");
            migrationBuilder.DropIndex(name: "IX_OrderItems_InventoryUnitId", schema: "sales", table: "OrderItems");
            migrationBuilder.DropIndex(name: "IX_OrderItems_PriceCalculationSnapshotId", schema: "sales", table: "OrderItems");
            migrationBuilder.DropIndex(name: "IX_Payments_IdempotencyKeyHash", schema: "billing", table: "Payments");
            migrationBuilder.DropIndex(name: "IX_Payments_OrderId", schema: "billing", table: "Payments");
            migrationBuilder.DropIndex(name: "IX_Payments_PaymentGatewayId", schema: "billing", table: "Payments");

            migrationBuilder.DropColumn(name: "OrderItemId", schema: "inventory", table: "StockReservations");
            migrationBuilder.DropColumn(name: "InventoryUnitId", schema: "invoicing", table: "InvoiceItems");
            migrationBuilder.DropColumn(name: "GoldValueRials", schema: "invoicing", table: "InvoiceItems");
            migrationBuilder.DropColumn(name: "Karat", schema: "invoicing", table: "InvoiceItems");
            migrationBuilder.DropColumn(name: "MarketUnitPriceRials", schema: "invoicing", table: "InvoiceItems");
            migrationBuilder.DropColumn(name: "NetGoldWeightGrams", schema: "invoicing", table: "InvoiceItems");
            migrationBuilder.DropColumn(name: "OrderItemId", schema: "invoicing", table: "InvoiceItems");
            migrationBuilder.DropColumn(name: "PriceCalculationSnapshotId", schema: "invoicing", table: "InvoiceItems");
            migrationBuilder.DropColumn(name: "ProfitRials", schema: "invoicing", table: "InvoiceItems");
            migrationBuilder.DropColumn(name: "RoundingPolicy", schema: "invoicing", table: "InvoiceItems");
            migrationBuilder.DropColumn(name: "TaxRials", schema: "invoicing", table: "InvoiceItems");
            migrationBuilder.DropColumn(name: "WageRials", schema: "invoicing", table: "InvoiceItems");
            migrationBuilder.DropColumn(name: "PaymentId", schema: "invoicing", table: "Invoices");
            migrationBuilder.DropColumn(name: "CustomerAddressId", schema: "sales", table: "OrderAddressSnapshots");
            migrationBuilder.DropColumn(name: "GoldValueRials", schema: "sales", table: "OrderItems");
            migrationBuilder.DropColumn(name: "InventoryItemId", schema: "sales", table: "OrderItems");
            migrationBuilder.DropColumn(name: "InventoryUnitId", schema: "sales", table: "OrderItems");
            migrationBuilder.DropColumn(name: "Karat", schema: "sales", table: "OrderItems");
            migrationBuilder.DropColumn(name: "MarketUnitPriceRials", schema: "sales", table: "OrderItems");
            migrationBuilder.DropColumn(name: "NetGoldWeightGrams", schema: "sales", table: "OrderItems");
            migrationBuilder.DropColumn(name: "PriceCalculationSnapshotId", schema: "sales", table: "OrderItems");
            migrationBuilder.DropColumn(name: "ProfitRials", schema: "sales", table: "OrderItems");
            migrationBuilder.DropColumn(name: "RoundingPolicy", schema: "sales", table: "OrderItems");
            migrationBuilder.DropColumn(name: "TaxRials", schema: "sales", table: "OrderItems");
            migrationBuilder.DropColumn(name: "WageRials", schema: "sales", table: "OrderItems");
            migrationBuilder.DropColumn(name: "CustomerNameSnapshot", schema: "sales", table: "Orders");
            migrationBuilder.DropColumn(name: "CustomerNationalIdSnapshot", schema: "sales", table: "Orders");
            migrationBuilder.DropColumn(name: "CancelledAt", schema: "billing", table: "Payments");
            migrationBuilder.DropColumn(name: "IdempotencyKeyHash", schema: "billing", table: "Payments");
            migrationBuilder.DropColumn(name: "Method", schema: "billing", table: "Payments");
            migrationBuilder.DropColumn(name: "PaymentGatewayId", schema: "billing", table: "Payments");
            migrationBuilder.DropColumn(name: "RedirectUrl", schema: "billing", table: "PaymentAttempts");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_OrderId_InventoryItemId",
                schema: "inventory",
                table: "StockReservations",
                columns: new[] { "OrderId", "InventoryItemId" },
                unique: true,
                filter: "[Status] = 'Active'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Status",
                schema: "sales",
                table: "Orders",
                sql: "[Status] IN ('Pending', 'AwaitingPayment', 'Paid', 'Processing', 'Completed', 'Cancelled', 'Refunded')");
            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderStatusHistory_FromStatus",
                schema: "sales",
                table: "OrderStatusHistory",
                sql: "[FromStatus] IS NULL OR [FromStatus] IN ('Pending', 'AwaitingPayment', 'Paid', 'Processing', 'Completed', 'Cancelled', 'Refunded')");
            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderStatusHistory_ToStatus",
                schema: "sales",
                table: "OrderStatusHistory",
                sql: "[ToStatus] IN ('Pending', 'AwaitingPayment', 'Paid', 'Processing', 'Completed', 'Cancelled', 'Refunded')");
            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Status",
                schema: "billing",
                table: "Payments",
                sql: "[Status] IN ('Pending', 'Processing', 'Verified', 'Failed', 'Cancelled', 'Refunded')");
        }
    }
}
