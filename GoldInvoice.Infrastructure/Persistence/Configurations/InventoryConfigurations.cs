using GoldInvoice.Domain.Catalog;
using GoldInvoice.Domain.Inventory;
using GoldInvoice.Domain.Orders;
using GoldInvoice.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoldInvoice.Infrastructure.Persistence.Configurations;

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouses", DatabaseSchemas.Inventory, table =>
        {
            table.HasCheckConstraint(
                "CK_Warehouses_SoftDelete",
                "([IsDeleted] = 0 AND [DeletedAt] IS NULL) OR ([IsDeleted] = 1 AND [DeletedAt] IS NOT NULL)");
        });
        builder.ConfigureSoftDelete();
        builder.Property(warehouse => warehouse.Code).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(warehouse => warehouse.Name).HasMaxLength(200).IsRequired();
        builder.Property(warehouse => warehouse.IsActive).HasDefaultValue(true);
        builder.HasIndex(warehouse => warehouse.Code)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}

internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("InventoryItems", DatabaseSchemas.Inventory, table =>
        {
            table.HasCheckConstraint("CK_InventoryItems_OnHand", "[QuantityOnHand] >= 0");
            table.HasCheckConstraint("CK_InventoryItems_Reserved", "[QuantityReserved] >= 0");
            table.HasCheckConstraint(
                "CK_InventoryItems_Available",
                "[QuantityReserved] <= [QuantityOnHand]");
        });
        builder.ConfigureAuditable();
        builder.HasIndex(item => new { item.WarehouseId, item.ProductVariantId }).IsUnique();
        builder.HasIndex(item => new { item.ProductVariantId, item.QuantityOnHand });
        builder.HasAlternateKey(item => new { item.Id, item.WarehouseId, item.ProductVariantId });
        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(item => item.WarehouseId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(item => item.ProductVariantId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements", DatabaseSchemas.Inventory, table =>
        {
            table.HasCheckConstraint(
                "CK_StockMovements_Quantity",
                "[QuantityDelta] <> 0 OR [ReservedQuantityDelta] <> 0");
            table.HasCheckConstraint("CK_StockMovements_Balance", "[BalanceAfter] >= 0");
            table.HasCheckConstraint(
                "CK_StockMovements_ReservedBalance",
                "[ReservedBalanceAfter] >= 0 AND [ReservedBalanceAfter] <= [BalanceAfter]");
            table.HasCheckConstraint(
                "CK_StockMovements_Type",
                "[MovementType] IN ('InitialStock', 'Purchase', 'Reservation', 'ReservationReleased', 'ReservationConfirmed', 'Sale', 'Return', 'TransferOut', 'TransferIn', 'ManualAdjustment', 'Damage', 'Correction')");
        });
        builder.ConfigureAuditable();
        builder.Property(movement => movement.MovementType).ConfigureEnum();
        builder.Property(movement => movement.ReferenceType).HasMaxLength(100).IsUnicode(false);
        builder.Property(movement => movement.Reason).HasMaxLength(1000);
        builder.Property(movement => movement.OccurredAt).HasPrecision(7);
        builder.HasIndex(movement => new { movement.InventoryItemId, movement.OccurredAt });
        builder.HasIndex(movement => new { movement.ReferenceType, movement.ReferenceId });
        builder.HasIndex(movement => new { movement.InventoryUnitId, movement.OccurredAt })
            .HasFilter("[InventoryUnitId] IS NOT NULL");
        builder.HasOne<InventoryItem>()
            .WithMany()
            .HasForeignKey(movement => movement.InventoryItemId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<InventoryUnit>()
            .WithMany()
            .HasForeignKey(movement => movement.InventoryUnitId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("StockReservations", DatabaseSchemas.Inventory, table =>
        {
            table.HasCheckConstraint("CK_StockReservations_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint(
                "CK_StockReservations_InventoryUnitQuantity",
                "[InventoryUnitId] IS NULL OR [Quantity] = 1");
            table.HasCheckConstraint("CK_StockReservations_Expiry", "[ExpiresAt] > [CreatedAt]");
            table.HasCheckConstraint(
                "CK_StockReservations_Status",
                "[Status] IN ('Active', 'Confirmed', 'Released', 'Expired')");
        });
        builder.ConfigureAuditable();
        builder.Property(reservation => reservation.ReservationKey).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(reservation => reservation.Status).ConfigureEnum();
        builder.Property(reservation => reservation.ExpiresAt).HasPrecision(7);
        builder.Property(reservation => reservation.ConfirmedAt).HasPrecision(7);
        builder.Property(reservation => reservation.ReleasedAt).HasPrecision(7);
        builder.HasIndex(reservation => reservation.ReservationKey).IsUnique();
        builder.HasIndex(reservation => new { reservation.Status, reservation.ExpiresAt });
        builder.HasIndex(reservation => new { reservation.OrderId, reservation.InventoryItemId })
            .IsUnique()
            .HasFilter("[Status] = 'Active' AND [InventoryUnitId] IS NULL");
        builder.HasIndex(reservation => reservation.InventoryUnitId)
            .IsUnique()
            .HasFilter("[InventoryUnitId] IS NOT NULL AND [Status] = 'Active'");
        builder.HasIndex(reservation => reservation.OrderItemId)
            .IsUnique()
            .HasFilter("[OrderItemId] IS NOT NULL");
        builder.HasOne<InventoryItem>()
            .WithMany()
            .HasForeignKey(reservation => reservation.InventoryItemId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(reservation => reservation.OrderId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<OrderItem>()
            .WithMany()
            .HasForeignKey(reservation => reservation.OrderItemId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<InventoryUnit>()
            .WithMany()
            .HasForeignKey(reservation => reservation.InventoryUnitId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class InventoryUnitConfiguration : IEntityTypeConfiguration<InventoryUnit>
{
    public void Configure(EntityTypeBuilder<InventoryUnit> builder)
    {
        builder.ToTable("InventoryUnits", DatabaseSchemas.Inventory, table =>
        {
            table.HasCheckConstraint("CK_InventoryUnits_GrossWeight", "[ActualGrossWeight] > 0");
            table.HasCheckConstraint(
                "CK_InventoryUnits_NetWeight",
                "[ActualNetGoldWeight] > 0 AND [ActualNetGoldWeight] <= [ActualGrossWeight]");
            table.HasCheckConstraint("CK_InventoryUnits_Karat", "[Karat] IN (9, 10, 14, 18, 21, 22, 24)");
            table.HasCheckConstraint("CK_InventoryUnits_AcquisitionCost", "[AcquisitionCostRials] >= 0");
            table.HasCheckConstraint(
                "CK_InventoryUnits_Status",
                "[Status] IN ('Available', 'Reserved', 'Sold', 'Damaged', 'Returned', 'Transferred', 'Inactive')");
            table.HasCheckConstraint(
                "CK_InventoryUnits_SoldState",
                "([Status] IN ('Sold', 'Returned') AND [SoldAt] IS NOT NULL) OR [Status] NOT IN ('Sold', 'Returned')");
        });
        builder.ConfigureAuditable();
        builder.Property(unit => unit.SerialNumber).HasMaxLength(100).IsUnicode(false);
        builder.Property(unit => unit.Barcode).HasMaxLength(100).IsUnicode(false);
        builder.Property(unit => unit.ActualGrossWeight).HasPrecision(18, 3);
        builder.Property(unit => unit.ActualNetGoldWeight).HasPrecision(18, 3);
        builder.Property(unit => unit.Status).ConfigureEnum();
        builder.Property(unit => unit.ReceivedAt).HasPrecision(7);
        builder.Property(unit => unit.SoldAt).HasPrecision(7);
        builder.HasIndex(unit => unit.SerialNumber)
            .IsUnique()
            .HasFilter("[SerialNumber] IS NOT NULL");
        builder.HasIndex(unit => unit.Barcode)
            .IsUnique()
            .HasFilter("[Barcode] IS NOT NULL");
        builder.HasIndex(unit => new { unit.WarehouseId, unit.Status, unit.ProductVariantId });
        builder.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(unit => new { unit.ProductId, unit.ProductVariantId })
            .HasPrincipalKey(variant => new { variant.ProductId, variant.Id })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<InventoryItem>()
            .WithMany()
            .HasForeignKey(unit => new { unit.InventoryItemId, unit.WarehouseId, unit.ProductVariantId })
            .HasPrincipalKey(item => new { item.Id, item.WarehouseId, item.ProductVariantId })
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class InventoryAdjustmentConfiguration : IEntityTypeConfiguration<InventoryAdjustment>
{
    public void Configure(EntityTypeBuilder<InventoryAdjustment> builder)
    {
        builder.ToTable("InventoryAdjustments", DatabaseSchemas.Inventory, table =>
        {
            table.HasCheckConstraint("CK_InventoryAdjustments_Quantity", "[QuantityDelta] <> 0");
        });
        builder.ConfigureAuditable();
        builder.Property(adjustment => adjustment.Reason).HasMaxLength(1000).IsRequired();
        builder.HasIndex(adjustment => adjustment.StockMovementId).IsUnique();
        builder.HasIndex(adjustment => new { adjustment.InventoryItemId, adjustment.CreatedAt });
        builder.HasOne<InventoryItem>()
            .WithMany()
            .HasForeignKey(adjustment => adjustment.InventoryItemId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<StockMovement>()
            .WithMany()
            .HasForeignKey(adjustment => adjustment.StockMovementId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(adjustment => adjustment.ApprovedBy)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
