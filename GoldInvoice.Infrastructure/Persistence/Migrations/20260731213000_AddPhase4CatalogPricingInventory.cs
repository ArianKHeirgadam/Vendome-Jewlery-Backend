using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoldInvoice.Infrastructure.Persistence.Migrations;

public partial class AddPhase4CatalogPricingInventory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "pricing");

        migrationBuilder.DropCheckConstraint(
            name: "CK_StockMovements_Quantity",
            schema: "inventory",
            table: "StockMovements");

        migrationBuilder.DropCheckConstraint(
            name: "CK_StockMovements_Type",
            schema: "inventory",
            table: "StockMovements");

        migrationBuilder.DropIndex(
            name: "IX_StockReservations_OrderId_InventoryItemId",
            schema: "inventory",
            table: "StockReservations");

        migrationBuilder.AddColumn<Guid>(
            name: "ProductCategoryId",
            schema: "catalog",
            table: "Products",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "InventoryUnitId",
            schema: "inventory",
            table: "StockMovements",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ReservedBalanceAfter",
            schema: "inventory",
            table: "StockMovements",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "ReservedQuantityDelta",
            schema: "inventory",
            table: "StockMovements",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<Guid>(
            name: "InventoryUnitId",
            schema: "inventory",
            table: "StockReservations",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddUniqueConstraint(
            name: "AK_InventoryItems_Id_WarehouseId_ProductVariantId",
            schema: "inventory",
            table: "InventoryItems",
            columns: new[] { "Id", "WarehouseId", "ProductVariantId" });

        migrationBuilder.CreateTable(
            name: "ProductCategories",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Slug = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                ParentCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                DisplayOrder = table.Column<int>(type: "int", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProductCategories", x => x.Id);
                table.CheckConstraint("CK_ProductCategories_DisplayOrder", "[DisplayOrder] >= 0");
                table.CheckConstraint("CK_ProductCategories_Parent", "[ParentCategoryId] IS NULL OR [ParentCategoryId] <> [Id]");
                table.ForeignKey(
                    name: "FK_ProductCategories_ProductCategories_ParentCategoryId",
                    column: x => x.ParentCategoryId,
                    principalSchema: "catalog",
                    principalTable: "ProductCategories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "GoldProductDetails",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Karat = table.Column<int>(type: "int", nullable: false),
                GrossWeight = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                NetGoldWeight = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                StoneWeight = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                OtherMaterialWeight = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                ManufacturingWageType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                ManufacturingWageAmountRials = table.Column<long>(type: "bigint", nullable: true),
                ManufacturingWagePercentage = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                ProfitPercentage = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                TaxPercentage = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                HasStone = table.Column<bool>(type: "bit", nullable: false),
                IsWeightVariable = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GoldProductDetails", x => x.Id);
                table.CheckConstraint("CK_GoldProductDetails_ComponentWeights", "[StoneWeight] >= 0 AND [OtherMaterialWeight] >= 0 AND ([NetGoldWeight] + [StoneWeight] + [OtherMaterialWeight]) <= [GrossWeight]");
                table.CheckConstraint("CK_GoldProductDetails_GrossWeight", "[GrossWeight] > 0");
                table.CheckConstraint("CK_GoldProductDetails_Karat", "[Karat] IN (9, 10, 14, 18, 21, 22, 24)");
                table.CheckConstraint("CK_GoldProductDetails_NetGoldWeight", "[NetGoldWeight] > 0");
                table.CheckConstraint("CK_GoldProductDetails_Percentages", "[ProfitPercentage] BETWEEN 0 AND 100 AND [TaxPercentage] BETWEEN 0 AND 100");
                table.CheckConstraint("CK_GoldProductDetails_StoneState", "([HasStone] = 1 AND [StoneWeight] > 0) OR ([HasStone] = 0 AND [StoneWeight] = 0)");
                table.CheckConstraint("CK_GoldProductDetails_Wage", "([ManufacturingWageType] IN ('FixedRials', 'PerGramRials') AND [ManufacturingWageAmountRials] IS NOT NULL AND [ManufacturingWageAmountRials] >= 0 AND [ManufacturingWagePercentage] IS NULL) OR ([ManufacturingWageType] = 'PercentageOfGoldValue' AND [ManufacturingWageAmountRials] IS NULL AND [ManufacturingWagePercentage] BETWEEN 0 AND 100)");
                table.ForeignKey(
                    name: "FK_GoldProductDetails_ProductVariants_ProductVariantId",
                    column: x => x.ProductVariantId,
                    principalSchema: "catalog",
                    principalTable: "ProductVariants",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "MarketPriceSources",
            schema: "pricing",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                ProviderCode = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                Priority = table.Column<int>(type: "int", nullable: false),
                BaseUrl = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                ConfigurationReference = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                LastSuccessfulFetchAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                LastFailureAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MarketPriceSources", x => x.Id);
                table.CheckConstraint("CK_MarketPriceSources_Priority", "[Priority] >= 0");
            });

        migrationBuilder.CreateTable(
            name: "ProductPricingRules",
            schema: "pricing",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PricingMethod = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                GoldMarketPriceType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                FixedPriceRials = table.Column<long>(type: "bigint", nullable: true),
                FixedGoldPricePerGramRials = table.Column<long>(type: "bigint", nullable: true),
                WageType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                WageAmountRials = table.Column<long>(type: "bigint", nullable: true),
                WagePercentage = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                ProfitPercentage = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                TaxPercentage = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                EffectiveFrom = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                EffectiveTo = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProductPricingRules", x => x.Id);
                table.CheckConstraint("CK_ProductPricingRules_Amounts", "([FixedPriceRials] IS NULL OR [FixedPriceRials] >= 0) AND ([FixedGoldPricePerGramRials] IS NULL OR [FixedGoldPricePerGramRials] >= 0)");
                table.CheckConstraint("CK_ProductPricingRules_MethodInputs", "([PricingMethod] = 'FixedPrice' AND [FixedPriceRials] > 0) OR ([PricingMethod] = 'WeightBased' AND [FixedGoldPricePerGramRials] > 0) OR ([PricingMethod] = 'MarketBased' AND [GoldMarketPriceType] IN ('Gold18K', 'Gold24K')) OR ([PricingMethod] = 'ManualReview')");
                table.CheckConstraint("CK_ProductPricingRules_Percentages", "[ProfitPercentage] BETWEEN 0 AND 100 AND [TaxPercentage] BETWEEN 0 AND 100");
                table.CheckConstraint("CK_ProductPricingRules_Wage", "([WageType] IN ('FixedRials', 'PerGramRials') AND [WageAmountRials] IS NOT NULL AND [WageAmountRials] >= 0 AND [WagePercentage] IS NULL) OR ([WageType] = 'PercentageOfGoldValue' AND [WageAmountRials] IS NULL AND [WagePercentage] BETWEEN 0 AND 100)");
                table.CheckConstraint("CK_ProductPricingRules_Window", "[EffectiveTo] IS NULL OR [EffectiveTo] > [EffectiveFrom]");
                table.ForeignKey(
                    name: "FK_ProductPricingRules_ProductVariants_ProductVariantId",
                    column: x => x.ProductVariantId,
                    principalSchema: "catalog",
                    principalTable: "ProductVariants",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "InventoryUnits",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InventoryItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SerialNumber = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                Barcode = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                ActualGrossWeight = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                ActualNetGoldWeight = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                Karat = table.Column<int>(type: "int", nullable: false),
                AcquisitionCostRials = table.Column<long>(type: "bigint", nullable: false),
                Status = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                SoldAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InventoryUnits", x => x.Id);
                table.CheckConstraint("CK_InventoryUnits_AcquisitionCost", "[AcquisitionCostRials] >= 0");
                table.CheckConstraint("CK_InventoryUnits_GrossWeight", "[ActualGrossWeight] > 0");
                table.CheckConstraint("CK_InventoryUnits_Karat", "[Karat] IN (9, 10, 14, 18, 21, 22, 24)");
                table.CheckConstraint("CK_InventoryUnits_NetWeight", "[ActualNetGoldWeight] > 0 AND [ActualNetGoldWeight] <= [ActualGrossWeight]");
                table.CheckConstraint("CK_InventoryUnits_SoldState", "([Status] IN ('Sold', 'Returned') AND [SoldAt] IS NOT NULL) OR [Status] NOT IN ('Sold', 'Returned')");
                table.CheckConstraint("CK_InventoryUnits_Status", "[Status] IN ('Available', 'Reserved', 'Sold', 'Damaged', 'Returned', 'Transferred', 'Inactive')");
                table.ForeignKey(
                    name: "FK_InventoryUnits_InventoryItems_InventoryItemId_WarehouseId_ProductVariantId",
                    columns: x => new { x.InventoryItemId, x.WarehouseId, x.ProductVariantId },
                    principalSchema: "inventory",
                    principalTable: "InventoryItems",
                    principalColumns: new[] { "Id", "WarehouseId", "ProductVariantId" });
                table.ForeignKey(
                    name: "FK_InventoryUnits_ProductVariants_ProductId_ProductVariantId",
                    columns: x => new { x.ProductId, x.ProductVariantId },
                    principalSchema: "catalog",
                    principalTable: "ProductVariants",
                    principalColumns: new[] { "ProductId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "MarketPriceSnapshots",
            schema: "pricing",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PriceType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                BuyPriceRials = table.Column<long>(type: "bigint", nullable: false),
                SellPriceRials = table.Column<long>(type: "bigint", nullable: false),
                CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                ProviderTimestamp = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                IsValid = table.Column<bool>(type: "bit", nullable: false),
                ValidationStatus = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                RawPayloadHash = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MarketPriceSnapshots", x => x.Id);
                table.CheckConstraint("CK_MarketPriceSnapshots_Prices", "[BuyPriceRials] >= 0 AND [SellPriceRials] >= 0");
                table.CheckConstraint("CK_MarketPriceSnapshots_ValidState", "([IsValid] = 1 AND [ValidationStatus] = 'Accepted' AND [BuyPriceRials] > 0 AND [SellPriceRials] >= [BuyPriceRials]) OR ([IsValid] = 0 AND [ValidationStatus] <> 'Accepted')");
                table.ForeignKey(
                    name: "FK_MarketPriceSnapshots_MarketPriceSources_SourceId",
                    column: x => x.SourceId,
                    principalSchema: "pricing",
                    principalTable: "MarketPriceSources",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "PriceCalculationSnapshots",
            schema: "pricing",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PricingRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                MarketPriceSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                PricingMethod = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                GrossWeight = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                NetGoldWeight = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                Karat = table.Column<int>(type: "int", nullable: false),
                MarketUnitPriceRials = table.Column<long>(type: "bigint", nullable: false),
                GoldValueRials = table.Column<long>(type: "bigint", nullable: false),
                WageRials = table.Column<long>(type: "bigint", nullable: false),
                ProfitRials = table.Column<long>(type: "bigint", nullable: false),
                TaxRials = table.Column<long>(type: "bigint", nullable: false),
                FinalPriceRials = table.Column<long>(type: "bigint", nullable: false),
                CalculatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                RoundingPolicy = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PriceCalculationSnapshots", x => x.Id);
                table.CheckConstraint("CK_PriceCalculationSnapshots_Amounts", "[MarketUnitPriceRials] >= 0 AND [GoldValueRials] >= 0 AND [WageRials] >= 0 AND [ProfitRials] >= 0 AND [TaxRials] >= 0 AND [FinalPriceRials] = [GoldValueRials] + [WageRials] + [ProfitRials] + [TaxRials]");
                table.CheckConstraint("CK_PriceCalculationSnapshots_Karat", "[Karat] IN (9, 10, 14, 18, 21, 22, 24)");
                table.CheckConstraint("CK_PriceCalculationSnapshots_Weights", "[GrossWeight] > 0 AND [NetGoldWeight] > 0 AND [NetGoldWeight] <= [GrossWeight]");
                table.ForeignKey(
                    name: "FK_PriceCalculationSnapshots_MarketPriceSnapshots_MarketPriceSnapshotId",
                    column: x => x.MarketPriceSnapshotId,
                    principalSchema: "pricing",
                    principalTable: "MarketPriceSnapshots",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_PriceCalculationSnapshots_ProductPricingRules_PricingRuleId",
                    column: x => x.PricingRuleId,
                    principalSchema: "pricing",
                    principalTable: "ProductPricingRules",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_PriceCalculationSnapshots_ProductVariants_ProductVariantId",
                    column: x => x.ProductVariantId,
                    principalSchema: "catalog",
                    principalTable: "ProductVariants",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateIndex(
            name: "IX_Products_ProductCategoryId_IsActive",
            schema: "catalog",
            table: "Products",
            columns: new[] { "ProductCategoryId", "IsActive" },
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_StockMovements_InventoryUnitId_OccurredAt",
            schema: "inventory",
            table: "StockMovements",
            columns: new[] { "InventoryUnitId", "OccurredAt" },
            filter: "[InventoryUnitId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_StockReservations_InventoryUnitId",
            schema: "inventory",
            table: "StockReservations",
            column: "InventoryUnitId",
            unique: true,
            filter: "[InventoryUnitId] IS NOT NULL AND [Status] = 'Active'");

        migrationBuilder.CreateIndex(
            name: "IX_StockReservations_OrderId_InventoryItemId",
            schema: "inventory",
            table: "StockReservations",
            columns: new[] { "OrderId", "InventoryItemId" },
            unique: true,
            filter: "[Status] = 'Active'");

        migrationBuilder.CreateIndex(
            name: "IX_GoldProductDetails_ProductVariantId",
            schema: "catalog",
            table: "GoldProductDetails",
            column: "ProductVariantId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProductCategories_ParentCategoryId_DisplayOrder_Name",
            schema: "catalog",
            table: "ProductCategories",
            columns: new[] { "ParentCategoryId", "DisplayOrder", "Name" });

        migrationBuilder.CreateIndex(
            name: "IX_ProductCategories_Slug",
            schema: "catalog",
            table: "ProductCategories",
            column: "Slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_InventoryUnits_Barcode",
            schema: "inventory",
            table: "InventoryUnits",
            column: "Barcode",
            unique: true,
            filter: "[Barcode] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_InventoryUnits_InventoryItemId_WarehouseId_ProductVariantId",
            schema: "inventory",
            table: "InventoryUnits",
            columns: new[] { "InventoryItemId", "WarehouseId", "ProductVariantId" });

        migrationBuilder.CreateIndex(
            name: "IX_InventoryUnits_ProductId_ProductVariantId",
            schema: "inventory",
            table: "InventoryUnits",
            columns: new[] { "ProductId", "ProductVariantId" });

        migrationBuilder.CreateIndex(
            name: "IX_InventoryUnits_SerialNumber",
            schema: "inventory",
            table: "InventoryUnits",
            column: "SerialNumber",
            unique: true,
            filter: "[SerialNumber] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_InventoryUnits_WarehouseId_Status_ProductVariantId",
            schema: "inventory",
            table: "InventoryUnits",
            columns: new[] { "WarehouseId", "Status", "ProductVariantId" });

        migrationBuilder.CreateIndex(
            name: "IX_MarketPriceSnapshots_PriceType_IsValid_CapturedAt",
            schema: "pricing",
            table: "MarketPriceSnapshots",
            columns: new[] { "PriceType", "IsValid", "CapturedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_MarketPriceSnapshots_SourceId_PriceType_CapturedAt",
            schema: "pricing",
            table: "MarketPriceSnapshots",
            columns: new[] { "SourceId", "PriceType", "CapturedAt" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MarketPriceSnapshots_SourceId_PriceType_RawPayloadHash",
            schema: "pricing",
            table: "MarketPriceSnapshots",
            columns: new[] { "SourceId", "PriceType", "RawPayloadHash" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MarketPriceSources_IsActive_Priority",
            schema: "pricing",
            table: "MarketPriceSources",
            columns: new[] { "IsActive", "Priority" });

        migrationBuilder.CreateIndex(
            name: "IX_MarketPriceSources_ProviderCode",
            schema: "pricing",
            table: "MarketPriceSources",
            column: "ProviderCode",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PriceCalculationSnapshots_MarketPriceSnapshotId",
            schema: "pricing",
            table: "PriceCalculationSnapshots",
            column: "MarketPriceSnapshotId",
            filter: "[MarketPriceSnapshotId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_PriceCalculationSnapshots_PricingRuleId",
            schema: "pricing",
            table: "PriceCalculationSnapshots",
            column: "PricingRuleId");

        migrationBuilder.CreateIndex(
            name: "IX_PriceCalculationSnapshots_ProductVariantId_CalculatedAt",
            schema: "pricing",
            table: "PriceCalculationSnapshots",
            columns: new[] { "ProductVariantId", "CalculatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_ProductPricingRules_ProductVariantId_IsActive_EffectiveFrom_EffectiveTo",
            schema: "pricing",
            table: "ProductPricingRules",
            columns: new[] { "ProductVariantId", "IsActive", "EffectiveFrom", "EffectiveTo" });

        migrationBuilder.AddCheckConstraint(
            name: "CK_StockMovements_Quantity",
            schema: "inventory",
            table: "StockMovements",
            sql: "[QuantityDelta] <> 0 OR [ReservedQuantityDelta] <> 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_StockMovements_ReservedBalance",
            schema: "inventory",
            table: "StockMovements",
            sql: "[ReservedBalanceAfter] >= 0 AND [ReservedBalanceAfter] <= [BalanceAfter]");

        migrationBuilder.AddCheckConstraint(
            name: "CK_StockMovements_Type",
            schema: "inventory",
            table: "StockMovements",
            sql: "[MovementType] IN ('InitialStock', 'Purchase', 'Reservation', 'ReservationReleased', 'ReservationConfirmed', 'Sale', 'Return', 'TransferOut', 'TransferIn', 'ManualAdjustment', 'Damage', 'Correction')");

        migrationBuilder.AddCheckConstraint(
            name: "CK_StockReservations_InventoryUnitQuantity",
            schema: "inventory",
            table: "StockReservations",
            sql: "[InventoryUnitId] IS NULL OR [Quantity] = 1");

        migrationBuilder.AddForeignKey(
            name: "FK_Products_ProductCategories_ProductCategoryId",
            schema: "catalog",
            table: "Products",
            column: "ProductCategoryId",
            principalSchema: "catalog",
            principalTable: "ProductCategories",
            principalColumn: "Id");

        migrationBuilder.AddForeignKey(
            name: "FK_StockMovements_InventoryUnits_InventoryUnitId",
            schema: "inventory",
            table: "StockMovements",
            column: "InventoryUnitId",
            principalSchema: "inventory",
            principalTable: "InventoryUnits",
            principalColumn: "Id");

        migrationBuilder.AddForeignKey(
            name: "FK_StockReservations_InventoryUnits_InventoryUnitId",
            schema: "inventory",
            table: "StockReservations",
            column: "InventoryUnitId",
            principalSchema: "inventory",
            principalTable: "InventoryUnits",
            principalColumn: "Id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Products_ProductCategories_ProductCategoryId",
            schema: "catalog",
            table: "Products");

        migrationBuilder.DropForeignKey(
            name: "FK_StockMovements_InventoryUnits_InventoryUnitId",
            schema: "inventory",
            table: "StockMovements");

        migrationBuilder.DropForeignKey(
            name: "FK_StockReservations_InventoryUnits_InventoryUnitId",
            schema: "inventory",
            table: "StockReservations");

        migrationBuilder.DropCheckConstraint(
            name: "CK_StockMovements_Quantity",
            schema: "inventory",
            table: "StockMovements");

        migrationBuilder.DropCheckConstraint(
            name: "CK_StockMovements_ReservedBalance",
            schema: "inventory",
            table: "StockMovements");

        migrationBuilder.DropCheckConstraint(
            name: "CK_StockMovements_Type",
            schema: "inventory",
            table: "StockMovements");

        migrationBuilder.DropCheckConstraint(
            name: "CK_StockReservations_InventoryUnitQuantity",
            schema: "inventory",
            table: "StockReservations");

        migrationBuilder.DropTable(name: "GoldProductDetails", schema: "catalog");
        migrationBuilder.DropTable(name: "PriceCalculationSnapshots", schema: "pricing");
        migrationBuilder.DropTable(name: "InventoryUnits", schema: "inventory");
        migrationBuilder.DropTable(name: "ProductCategories", schema: "catalog");
        migrationBuilder.DropTable(name: "MarketPriceSnapshots", schema: "pricing");
        migrationBuilder.DropTable(name: "ProductPricingRules", schema: "pricing");
        migrationBuilder.DropTable(name: "MarketPriceSources", schema: "pricing");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_InventoryItems_Id_WarehouseId_ProductVariantId",
            schema: "inventory",
            table: "InventoryItems");

        migrationBuilder.DropIndex(
            name: "IX_Products_ProductCategoryId_IsActive",
            schema: "catalog",
            table: "Products");

        migrationBuilder.DropIndex(
            name: "IX_StockMovements_InventoryUnitId_OccurredAt",
            schema: "inventory",
            table: "StockMovements");

        migrationBuilder.DropIndex(
            name: "IX_StockReservations_InventoryUnitId",
            schema: "inventory",
            table: "StockReservations");

        migrationBuilder.DropIndex(
            name: "IX_StockReservations_OrderId_InventoryItemId",
            schema: "inventory",
            table: "StockReservations");

        migrationBuilder.DropColumn(
            name: "ProductCategoryId",
            schema: "catalog",
            table: "Products");

        migrationBuilder.DropColumn(
            name: "InventoryUnitId",
            schema: "inventory",
            table: "StockMovements");

        migrationBuilder.DropColumn(
            name: "ReservedBalanceAfter",
            schema: "inventory",
            table: "StockMovements");

        migrationBuilder.DropColumn(
            name: "ReservedQuantityDelta",
            schema: "inventory",
            table: "StockMovements");

        migrationBuilder.DropColumn(
            name: "InventoryUnitId",
            schema: "inventory",
            table: "StockReservations");

        migrationBuilder.CreateIndex(
            name: "IX_StockReservations_OrderId_InventoryItemId",
            schema: "inventory",
            table: "StockReservations",
            columns: new[] { "OrderId", "InventoryItemId" },
            unique: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_StockMovements_Quantity",
            schema: "inventory",
            table: "StockMovements",
            sql: "[QuantityDelta] <> 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_StockMovements_Type",
            schema: "inventory",
            table: "StockMovements",
            sql: "[MovementType] IN ('InitialStock', 'Purchase', 'Reservation', 'ReservationReleased', 'Sale', 'Return', 'ManualAdjustment', 'Damage', 'Correction')");
    }
}
