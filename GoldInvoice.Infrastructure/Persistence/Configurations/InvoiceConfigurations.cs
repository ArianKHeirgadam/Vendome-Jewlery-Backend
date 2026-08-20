using GoldInvoice.Domain.Invoicing;
using GoldInvoice.Domain.Inventory;
using GoldInvoice.Domain.Orders;
using GoldInvoice.Domain.Payments;
using GoldInvoice.Domain.Platform;
using GoldInvoice.Domain.Pricing;
using GoldInvoice.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoldInvoice.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceSequenceConfiguration : IEntityTypeConfiguration<InvoiceSequence>
{
    public void Configure(EntityTypeBuilder<InvoiceSequence> builder)
    {
        builder.ToTable("InvoiceSequences", DatabaseSchemas.Invoicing, table =>
        {
            table.HasCheckConstraint("CK_InvoiceSequences_NextValue", "[NextValue] > 0");
        });
        builder.ConfigureAuditable();
        builder.Property(sequence => sequence.Series).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(sequence => sequence.Prefix).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(sequence => sequence.LastIssuedAt).HasPrecision(7);
        builder.HasIndex(sequence => sequence.Series).IsUnique();
        builder.HasIndex(sequence => sequence.Prefix).IsUnique();
    }
}

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices", DatabaseSchemas.Invoicing, table =>
        {
            table.HasCheckConstraint("CK_Invoices_Status", "[Status] IN ('Issued', 'Voided')");
            table.HasCheckConstraint(
                "CK_Invoices_Amounts",
                "[SubtotalRials] >= 0 AND [DiscountRials] >= 0 AND [DiscountRials] <= [SubtotalRials] AND [ShippingRials] >= 0 AND [GrandTotalRials] = [SubtotalRials] - [DiscountRials] + [ShippingRials]");
            table.HasCheckConstraint(
                "CK_Invoices_Void",
                "([Status] = 'Issued' AND [VoidedAt] IS NULL) OR ([Status] = 'Voided' AND [VoidedAt] IS NOT NULL AND [VoidReason] IS NOT NULL)");
        });
        builder.ConfigureAuditable();
        builder.Property(invoice => invoice.InvoiceNumber).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(invoice => invoice.Status).ConfigureEnum();
        builder.Property(invoice => invoice.IssuedAt).HasPrecision(7);
        builder.Property(invoice => invoice.CustomerNameSnapshot).HasMaxLength(200);
        builder.Property(invoice => invoice.CustomerNationalIdSnapshot).HasMaxLength(32).IsUnicode(false);
        builder.Property(invoice => invoice.VoidedAt).HasPrecision(7);
        builder.Property(invoice => invoice.VoidReason).HasMaxLength(1000);
        builder.HasIndex(invoice => invoice.OrderId).IsUnique();
        builder.HasIndex(invoice => invoice.PaymentId)
            .IsUnique()
            .HasFilter("[PaymentId] IS NOT NULL");
        builder.HasIndex(invoice => invoice.InvoiceNumber).IsUnique();
        builder.HasIndex(invoice => new { invoice.CustomerId, invoice.IssuedAt });
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(invoice => invoice.OrderId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(invoice => invoice.PaymentId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(invoice => invoice.CustomerId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class InvoiceItemConfiguration : IEntityTypeConfiguration<InvoiceItem>
{
    public void Configure(EntityTypeBuilder<InvoiceItem> builder)
    {
        builder.ToTable("InvoiceItems", DatabaseSchemas.Invoicing, table =>
        {
            table.HasCheckConstraint("CK_InvoiceItems_LineNumber", "[LineNumber] > 0");
            table.HasCheckConstraint("CK_InvoiceItems_Weight", "[WeightGrams] > 0");
            table.HasCheckConstraint("CK_InvoiceItems_Purity", "[Purity] BETWEEN 1 AND 1000");
            table.HasCheckConstraint(
                "CK_InvoiceItems_Amounts",
                "[UnitPriceRials] >= 0 AND [Quantity] > 0 AND [LineTotalRials] = [UnitPriceRials] * [Quantity]");
            table.HasCheckConstraint(
                "CK_InvoiceItems_AcquisitionCost",
                "([AcquisitionUnitCostRials] IS NULL AND [AcquisitionTotalCostRials] IS NULL AND [GrossProfitRials] IS NULL) OR ([AcquisitionUnitCostRials] >= 0 AND [AcquisitionTotalCostRials] = [AcquisitionUnitCostRials] * [Quantity] AND [GrossProfitRials] = [LineTotalRials] - [AcquisitionTotalCostRials])");
            table.HasCheckConstraint(
                "CK_InvoiceItems_PriceSnapshot",
                "([OrderItemId] IS NULL AND [PriceCalculationSnapshotId] IS NULL AND [InventoryUnitId] IS NULL AND [NetGoldWeightGrams] IS NULL AND [Karat] IS NULL AND [MarketUnitPriceRials] IS NULL AND [GoldValueRials] IS NULL AND [WageRials] IS NULL AND [ProfitRials] IS NULL AND [TaxRials] IS NULL AND [RoundingPolicy] IS NULL) OR ([OrderItemId] IS NOT NULL AND [PriceCalculationSnapshotId] IS NOT NULL AND [NetGoldWeightGrams] > 0 AND [NetGoldWeightGrams] <= [WeightGrams] AND [Karat] IN (9, 10, 14, 18, 21, 22, 24) AND [MarketUnitPriceRials] >= 0 AND [GoldValueRials] >= 0 AND [WageRials] >= 0 AND [ProfitRials] >= 0 AND [TaxRials] >= 0 AND [UnitPriceRials] = [GoldValueRials] + [WageRials] + [ProfitRials] + [TaxRials] AND [RoundingPolicy] IS NOT NULL)");
        });
        builder.ConfigureAuditable();
        builder.Property(item => item.Sku).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(item => item.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(item => item.VariantName).HasMaxLength(200).IsRequired();
        builder.Property(item => item.WeightGrams).HasPrecision(18, 3);
        builder.Property(item => item.NetGoldWeightGrams).HasPrecision(18, 3);
        builder.Property(item => item.MarketUnitPriceRials).HasColumnType("bigint");
        builder.Property(item => item.GoldValueRials).HasColumnType("bigint");
        builder.Property(item => item.WageRials).HasColumnType("bigint");
        builder.Property(item => item.ProfitRials).HasColumnType("bigint");
        builder.Property(item => item.TaxRials).HasColumnType("bigint");
        builder.Property(item => item.AcquisitionUnitCostRials).HasColumnType("bigint");
        builder.Property(item => item.AcquisitionTotalCostRials).HasColumnType("bigint");
        builder.Property(item => item.GrossProfitRials).HasColumnType("bigint");
        builder.Property(item => item.RoundingPolicy).HasMaxLength(100).IsUnicode(false);
        builder.HasIndex(item => new { item.InvoiceId, item.LineNumber }).IsUnique();
        builder.HasIndex(item => item.OrderItemId)
            .IsUnique()
            .HasFilter("[OrderItemId] IS NOT NULL");
        builder.HasIndex(item => item.PriceCalculationSnapshotId)
            .IsUnique()
            .HasFilter("[PriceCalculationSnapshotId] IS NOT NULL");
        builder.HasIndex(item => item.InventoryUnitId)
            .IsUnique()
            .HasFilter("[InventoryUnitId] IS NOT NULL");
        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(item => item.InvoiceId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<OrderItem>()
            .WithMany()
            .HasForeignKey(item => item.OrderItemId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<PriceCalculationSnapshot>()
            .WithMany()
            .HasForeignKey(item => item.PriceCalculationSnapshotId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<InventoryUnit>()
            .WithMany()
            .HasForeignKey(item => item.InventoryUnitId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class InvoiceAddressSnapshotConfiguration : IEntityTypeConfiguration<InvoiceAddressSnapshot>
{
    public void Configure(EntityTypeBuilder<InvoiceAddressSnapshot> builder)
    {
        builder.ToTable("InvoiceAddressSnapshots", DatabaseSchemas.Invoicing);
        builder.ConfigureAuditable();
        builder.Property(snapshot => snapshot.RecipientName).HasMaxLength(200).IsRequired();
        builder.Property(snapshot => snapshot.PhoneNumber).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(snapshot => snapshot.Province).HasMaxLength(100).IsRequired();
        builder.Property(snapshot => snapshot.City).HasMaxLength(100).IsRequired();
        builder.Property(snapshot => snapshot.PostalCode).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(snapshot => snapshot.AddressLine).HasMaxLength(1000).IsRequired();
        builder.HasIndex(snapshot => snapshot.InvoiceId).IsUnique();
        builder.HasIndex(snapshot => snapshot.OrderAddressSnapshotId).IsUnique();
        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.InvoiceId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<OrderAddressSnapshot>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.OrderAddressSnapshotId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class InvoiceStoreSnapshotConfiguration : IEntityTypeConfiguration<InvoiceStoreSnapshot>
{
    public void Configure(EntityTypeBuilder<InvoiceStoreSnapshot> builder)
    {
        builder.ToTable("InvoiceStoreSnapshots", DatabaseSchemas.Invoicing);
        builder.ConfigureAuditable();
        builder.Property(snapshot => snapshot.TradeName).HasMaxLength(200).IsRequired();
        builder.Property(snapshot => snapshot.LegalName).HasMaxLength(200).IsRequired();
        builder.Property(snapshot => snapshot.NationalId).HasMaxLength(32).IsUnicode(false);
        builder.Property(snapshot => snapshot.EconomicCode).HasMaxLength(32).IsUnicode(false);
        builder.Property(snapshot => snapshot.RegistrationNumber).HasMaxLength(32).IsUnicode(false);
        builder.Property(snapshot => snapshot.PhoneNumber).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(snapshot => snapshot.PostalCode).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(snapshot => snapshot.AddressLine).HasMaxLength(1000).IsRequired();
        builder.HasIndex(snapshot => snapshot.InvoiceId).IsUnique();
        builder.HasIndex(snapshot => snapshot.OrderStoreSnapshotId).IsUnique();
        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.InvoiceId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<OrderStoreSnapshot>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.OrderStoreSnapshotId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class InvoicePrintLogConfiguration : IEntityTypeConfiguration<InvoicePrintLog>
{
    public void Configure(EntityTypeBuilder<InvoicePrintLog> builder)
    {
        builder.ToTable("InvoicePrintLogs", DatabaseSchemas.Invoicing, table =>
        {
            table.HasCheckConstraint("CK_InvoicePrintLogs_Copies", "[Copies] > 0");
            table.HasCheckConstraint(
                "CK_InvoicePrintLogs_Status",
                "[Status] IN ('Requested', 'Succeeded', 'Failed')");
            table.HasCheckConstraint(
                "CK_InvoicePrintLogs_DeviceBinding",
                "([PrintJobId] IS NULL AND [DesktopDeviceId] IS NULL) OR ([PrintJobId] IS NOT NULL AND [DesktopDeviceId] IS NOT NULL)");
        });
        builder.ConfigureAuditable();
        builder.Property(log => log.Status).ConfigureEnum();
        builder.Property(log => log.ReprintReason).HasMaxLength(1000);
        builder.Property(log => log.PrinterName).HasMaxLength(300);
        builder.Property(log => log.CompletedAt).HasPrecision(7);
        builder.Property(log => log.FailureCode).HasMaxLength(100).IsUnicode(false);
        builder.HasIndex(log => new { log.InvoiceId, log.CreatedAt });
        builder.HasIndex(log => new { log.DesktopDeviceId, log.Status, log.CreatedAt });
        builder.HasIndex(log => log.PrintJobId)
            .HasFilter("[PrintJobId] IS NOT NULL");
        builder.HasOne<InvoicePrintJob>()
            .WithMany()
            .HasForeignKey(log => log.PrintJobId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(log => log.InvoiceId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<DesktopDevice>()
            .WithMany()
            .HasForeignKey(log => log.DesktopDeviceId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(log => log.RequestedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class InvoicePrintJobConfiguration : IEntityTypeConfiguration<InvoicePrintJob>
{
    public void Configure(EntityTypeBuilder<InvoicePrintJob> builder)
    {
        builder.ToTable("InvoicePrintJobs", DatabaseSchemas.Invoicing, table =>
        {
            table.HasCheckConstraint("CK_InvoicePrintJobs_Copies", "[Copies] > 0");
            table.HasCheckConstraint("CK_InvoicePrintJobs_RetryCount", "[RetryCount] >= 0");
            table.HasCheckConstraint(
                "CK_InvoicePrintJobs_Status",
                "[Status] IN ('Requested', 'Succeeded', 'Failed')");
            table.HasCheckConstraint(
                "CK_InvoicePrintJobs_Completion",
                "([Status] = 'Requested' AND [CompletedAt] IS NULL AND [FailureCode] IS NULL AND [PrintedAtPrinterName] IS NULL AND [PrintedByAgentSignature] IS NULL) OR ([Status] = 'Succeeded' AND [CompletedAt] IS NOT NULL AND [PrintedAtPrinterName] IS NOT NULL AND [PrintedByAgentSignature] IS NOT NULL) OR ([Status] = 'Failed' AND [CompletedAt] IS NOT NULL AND [FailureCode] IS NOT NULL AND [PrintedAtPrinterName] IS NULL AND [PrintedByAgentSignature] IS NULL)");
        });
        builder.ConfigureAuditable();
        builder.Property(job => job.Status).ConfigureEnum();
        builder.Property(job => job.ReprintReason).HasMaxLength(1000);
        builder.Property(job => job.IdempotencyKeyHash).HasMaxLength(128).IsUnicode(false);
        builder.Property(job => job.CompletedAt).HasPrecision(7);
        builder.Property(job => job.FailureCode).HasMaxLength(100).IsUnicode(false);
        builder.Property(job => job.PrintedAtPrinterName).HasMaxLength(300);
        builder.Property(job => job.PrintedByAgentSignature).HasMaxLength(512).IsUnicode(false);
        builder.HasIndex(job => new { job.DesktopDeviceId, job.Status, job.CreatedAt });
        builder.HasIndex(job => new { job.DesktopDeviceId, job.Status, job.IdempotencyKeyHash })
            .IsUnique()
            .HasFilter("[IdempotencyKeyHash] IS NOT NULL");
        builder.HasIndex(job => new { job.InvoiceId, job.CreatedAt });
        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(job => job.InvoiceId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<DesktopDevice>()
            .WithMany()
            .HasForeignKey(job => job.DesktopDeviceId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<DevicePrinter>()
            .WithMany()
            .HasForeignKey(job => job.DevicePrinterId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<PrintProfile>()
            .WithMany()
            .HasForeignKey(job => job.PrintProfileId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(job => job.RequestedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
