using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoldInvoice.Infrastructure.Persistence.Migrations;

[DbContext(typeof(GoldInvoiceDbContext))]
[Migration("20260813103000_AddPhase8SupplierPurchasesAndProfitSnapshots")]
public sealed class AddPhase8SupplierPurchasesAndProfitSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "AverageUnitCostRials",
            schema: "inventory",
            table: "InventoryItems",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<bool>(
            name: "HasAcquisitionCost",
            schema: "inventory",
            table: "InventoryItems",
            type: "bit",
            nullable: false,
            defaultValue: false);

        AddProfitSnapshotColumns(migrationBuilder, "sales", "OrderItems");
        AddProfitSnapshotColumns(migrationBuilder, "invoicing", "InvoiceItems");

        migrationBuilder.AddCheckConstraint(
            name: "CK_InventoryItems_AverageCost",
            schema: "inventory",
            table: "InventoryItems",
            sql: "[AverageUnitCostRials] >= 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_OrderItems_AcquisitionCost",
            schema: "sales",
            table: "OrderItems",
            sql: "([AcquisitionUnitCostRials] IS NULL AND [AcquisitionTotalCostRials] IS NULL AND [GrossProfitRials] IS NULL) OR ([AcquisitionUnitCostRials] >= 0 AND [AcquisitionTotalCostRials] = [AcquisitionUnitCostRials] * [Quantity] AND [GrossProfitRials] = [LineTotalRials] - [AcquisitionTotalCostRials])");

        migrationBuilder.AddCheckConstraint(
            name: "CK_InvoiceItems_AcquisitionCost",
            schema: "invoicing",
            table: "InvoiceItems",
            sql: "([AcquisitionUnitCostRials] IS NULL AND [AcquisitionTotalCostRials] IS NULL AND [GrossProfitRials] IS NULL) OR ([AcquisitionUnitCostRials] >= 0 AND [AcquisitionTotalCostRials] = [AcquisitionUnitCostRials] * [Quantity] AND [GrossProfitRials] = [LineTotalRials] - [AcquisitionTotalCostRials])");

        migrationBuilder.CreateTable(
            name: "SupplierPurchases",
            schema: "business",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PurchaseNumber = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InventoryItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StockMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PricingRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Quantity = table.Column<int>(type: "int", nullable: false),
                UnitCostRials = table.Column<long>(type: "bigint", nullable: false),
                TotalCostRials = table.Column<long>(type: "bigint", nullable: false),
                SellingUnitPriceRials = table.Column<long>(type: "bigint", nullable: false),
                PurchasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                SupplierReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SupplierPurchases", x => x.Id);
                table.CheckConstraint("CK_SupplierPurchases_Quantity", "[Quantity] > 0");
                table.CheckConstraint("CK_SupplierPurchases_Amounts", "[UnitCostRials] >= 0 AND [SellingUnitPriceRials] > 0 AND [TotalCostRials] = [UnitCostRials] * [Quantity]");
                table.ForeignKey(name: "FK_SupplierPurchases_Suppliers_SupplierId", column: x => x.SupplierId, principalSchema: "business", principalTable: "Suppliers", principalColumn: "Id");
                table.ForeignKey(name: "FK_SupplierPurchases_Warehouses_WarehouseId", column: x => x.WarehouseId, principalSchema: "inventory", principalTable: "Warehouses", principalColumn: "Id");
                table.ForeignKey(name: "FK_SupplierPurchases_ProductVariants_ProductVariantId", column: x => x.ProductVariantId, principalSchema: "catalog", principalTable: "ProductVariants", principalColumn: "Id");
                table.ForeignKey(name: "FK_SupplierPurchases_InventoryItems_InventoryItemId", column: x => x.InventoryItemId, principalSchema: "inventory", principalTable: "InventoryItems", principalColumn: "Id");
                table.ForeignKey(name: "FK_SupplierPurchases_StockMovements_StockMovementId", column: x => x.StockMovementId, principalSchema: "inventory", principalTable: "StockMovements", principalColumn: "Id");
                table.ForeignKey(name: "FK_SupplierPurchases_ProductPricingRules_PricingRuleId", column: x => x.PricingRuleId, principalSchema: "pricing", principalTable: "ProductPricingRules", principalColumn: "Id");
            });

        migrationBuilder.CreateIndex(name: "IX_SupplierPurchases_PurchaseNumber", schema: "business", table: "SupplierPurchases", column: "PurchaseNumber", unique: true);
        migrationBuilder.CreateIndex(name: "IX_SupplierPurchases_SupplierId_PurchasedAt", schema: "business", table: "SupplierPurchases", columns: new[] { "SupplierId", "PurchasedAt" });
        migrationBuilder.CreateIndex(name: "IX_SupplierPurchases_ProductVariantId_PurchasedAt", schema: "business", table: "SupplierPurchases", columns: new[] { "ProductVariantId", "PurchasedAt" });
        migrationBuilder.CreateIndex(name: "IX_SupplierPurchases_StockMovementId", schema: "business", table: "SupplierPurchases", column: "StockMovementId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_SupplierPurchases_WarehouseId", schema: "business", table: "SupplierPurchases", column: "WarehouseId");
        migrationBuilder.CreateIndex(name: "IX_SupplierPurchases_InventoryItemId", schema: "business", table: "SupplierPurchases", column: "InventoryItemId");
        migrationBuilder.CreateIndex(name: "IX_SupplierPurchases_PricingRuleId", schema: "business", table: "SupplierPurchases", column: "PricingRuleId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SupplierPurchases", schema: "business");
        migrationBuilder.DropCheckConstraint(name: "CK_InventoryItems_AverageCost", schema: "inventory", table: "InventoryItems");
        migrationBuilder.DropCheckConstraint(name: "CK_OrderItems_AcquisitionCost", schema: "sales", table: "OrderItems");
        migrationBuilder.DropCheckConstraint(name: "CK_InvoiceItems_AcquisitionCost", schema: "invoicing", table: "InvoiceItems");
        DropProfitSnapshotColumns(migrationBuilder, "sales", "OrderItems");
        DropProfitSnapshotColumns(migrationBuilder, "invoicing", "InvoiceItems");
        migrationBuilder.DropColumn(name: "AverageUnitCostRials", schema: "inventory", table: "InventoryItems");
        migrationBuilder.DropColumn(name: "HasAcquisitionCost", schema: "inventory", table: "InventoryItems");
    }

    private static void AddProfitSnapshotColumns(MigrationBuilder migrationBuilder, string schema, string table)
    {
        migrationBuilder.AddColumn<long>(name: "AcquisitionUnitCostRials", schema: schema, table: table, type: "bigint", nullable: true);
        migrationBuilder.AddColumn<long>(name: "AcquisitionTotalCostRials", schema: schema, table: table, type: "bigint", nullable: true);
        migrationBuilder.AddColumn<long>(name: "GrossProfitRials", schema: schema, table: table, type: "bigint", nullable: true);
    }

    private static void DropProfitSnapshotColumns(MigrationBuilder migrationBuilder, string schema, string table)
    {
        migrationBuilder.DropColumn(name: "AcquisitionUnitCostRials", schema: schema, table: table);
        migrationBuilder.DropColumn(name: "AcquisitionTotalCostRials", schema: schema, table: table);
        migrationBuilder.DropColumn(name: "GrossProfitRials", schema: schema, table: table);
    }
}
