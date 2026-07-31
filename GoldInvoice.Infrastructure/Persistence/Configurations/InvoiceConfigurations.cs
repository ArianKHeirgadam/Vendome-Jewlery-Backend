using GoldInvoice.Domain.Invoicing;
using GoldInvoice.Domain.Orders;
using GoldInvoice.Domain.Platform;
using GoldInvoice.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoldInvoice.Infrastructure.Persistence.Configurations;

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
        builder.HasIndex(invoice => invoice.InvoiceNumber).IsUnique();
        builder.HasIndex(invoice => new { invoice.CustomerId, invoice.IssuedAt });
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(invoice => invoice.OrderId)
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
        });
        builder.ConfigureAuditable();
        builder.Property(item => item.Sku).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(item => item.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(item => item.VariantName).HasMaxLength(200).IsRequired();
        builder.Property(item => item.WeightGrams).HasPrecision(18, 3);
        builder.HasIndex(item => new { item.InvoiceId, item.LineNumber }).IsUnique();
        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(item => item.InvoiceId)
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
        });
        builder.ConfigureAuditable();
        builder.Property(log => log.Status).ConfigureEnum();
        builder.Property(log => log.ReprintReason).HasMaxLength(1000);
        builder.Property(log => log.PrinterName).HasMaxLength(300);
        builder.Property(log => log.CompletedAt).HasPrecision(7);
        builder.Property(log => log.FailureCode).HasMaxLength(100).IsUnicode(false);
        builder.HasIndex(log => new { log.InvoiceId, log.CreatedAt });
        builder.HasIndex(log => new { log.DesktopDeviceId, log.Status, log.CreatedAt });
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
