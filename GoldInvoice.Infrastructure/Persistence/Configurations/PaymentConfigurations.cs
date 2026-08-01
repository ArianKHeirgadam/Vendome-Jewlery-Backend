using GoldInvoice.Domain.Orders;
using GoldInvoice.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoldInvoice.Infrastructure.Persistence.Configurations;

internal sealed class PaymentGatewayConfiguration : IEntityTypeConfiguration<PaymentGateway>
{
    public void Configure(EntityTypeBuilder<PaymentGateway> builder)
    {
        builder.ToTable("PaymentGateways", DatabaseSchemas.Billing);
        builder.ConfigureAuditable();
        builder.Property(gateway => gateway.Code).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(gateway => gateway.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(gateway => gateway.ProviderCode).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(gateway => gateway.ConfigurationReference).HasMaxLength(500).IsUnicode(false).IsRequired();
        builder.Property(gateway => gateway.IsActive).HasDefaultValue(true);
        builder.HasIndex(gateway => gateway.Code).IsUnique();
        builder.HasIndex(gateway => new { gateway.ProviderCode, gateway.IsActive });
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", DatabaseSchemas.Billing, table =>
        {
            table.HasCheckConstraint("CK_Payments_Amount", "[AmountRials] > 0");
            table.HasCheckConstraint(
                "CK_Payments_Status",
                "[Status] IN ('Pending', 'Processing', 'Verified', 'RequiresReview', 'Failed', 'Cancelled', 'Refunded')");
            table.HasCheckConstraint(
                "CK_Payments_Method",
                "[Method] IN ('OnlineGateway', 'Cash', 'PointOfSale', 'BankTransfer', 'CardToCard')");
        });
        builder.ConfigureAuditable();
        builder.Property(payment => payment.Provider).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(payment => payment.Method).ConfigureEnum();
        builder.Property(payment => payment.Status).ConfigureEnum();
        builder.Property(payment => payment.IdempotencyKeyHash).HasMaxLength(128).IsUnicode(false);
        builder.Property(payment => payment.Authority).HasMaxLength(200).IsUnicode(false);
        builder.Property(payment => payment.GatewayPaymentId).HasMaxLength(200).IsUnicode(false);
        builder.Property(payment => payment.VerifiedAt).HasPrecision(7);
        builder.Property(payment => payment.FailedAt).HasPrecision(7);
        builder.Property(payment => payment.CancelledAt).HasPrecision(7);
        builder.Property(payment => payment.FailureCode).HasMaxLength(100).IsUnicode(false);
        builder.HasIndex(payment => new { payment.OrderId, payment.CreatedAt });
        builder.HasIndex(payment => payment.OrderId)
            .IsUnique()
            .HasFilter("[Status] IN ('Pending', 'Processing', 'RequiresReview')");
        builder.HasIndex(payment => payment.IdempotencyKeyHash)
            .IsUnique()
            .HasFilter("[IdempotencyKeyHash] IS NOT NULL");
        builder.HasIndex(payment => new { payment.Provider, payment.Authority })
            .IsUnique()
            .HasFilter("[Authority] IS NOT NULL");
        builder.HasIndex(payment => new { payment.Provider, payment.GatewayPaymentId })
            .IsUnique()
            .HasFilter("[GatewayPaymentId] IS NOT NULL");
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(payment => payment.OrderId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<PaymentGateway>()
            .WithMany()
            .HasForeignKey(payment => payment.PaymentGatewayId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.ToTable("PaymentAttempts", DatabaseSchemas.Billing, table =>
        {
            table.HasCheckConstraint("CK_PaymentAttempts_Number", "[AttemptNumber] > 0");
            table.HasCheckConstraint("CK_PaymentAttempts_Amount", "[AmountRials] > 0");
            table.HasCheckConstraint(
                "CK_PaymentAttempts_Status",
                "[Status] IN ('Started', 'Redirected', 'Completed', 'Failed')");
        });
        builder.ConfigureAuditable();
        builder.Property(attempt => attempt.Status).ConfigureEnum();
        builder.Property(attempt => attempt.ProviderRequestId).HasMaxLength(200).IsUnicode(false);
        builder.Property(attempt => attempt.RedirectUrl).HasMaxLength(2000).IsUnicode(false);
        builder.Property(attempt => attempt.StartedAt).HasPrecision(7);
        builder.Property(attempt => attempt.CompletedAt).HasPrecision(7);
        builder.Property(attempt => attempt.FailureCode).HasMaxLength(100).IsUnicode(false);
        builder.Property(attempt => attempt.MaskedMetadataJson).HasColumnType("nvarchar(max)");
        builder.HasIndex(attempt => new { attempt.PaymentId, attempt.AttemptNumber }).IsUnique();
        builder.HasIndex(attempt => attempt.ProviderRequestId)
            .IsUnique()
            .HasFilter("[ProviderRequestId] IS NOT NULL");
        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(attempt => attempt.PaymentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class PaymentCallbackConfiguration : IEntityTypeConfiguration<PaymentCallback>
{
    public void Configure(EntityTypeBuilder<PaymentCallback> builder)
    {
        builder.ToTable("PaymentCallbacks", DatabaseSchemas.Billing);
        builder.ConfigureAuditable();
        builder.Property(callback => callback.Provider).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(callback => callback.ExternalCallbackId).HasMaxLength(200).IsUnicode(false).IsRequired();
        builder.Property(callback => callback.PayloadHash).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(callback => callback.MaskedPayloadJson).HasColumnType("nvarchar(max)");
        builder.Property(callback => callback.ProcessingResult).HasMaxLength(500);
        builder.Property(callback => callback.ReceivedAt).HasPrecision(7);
        builder.HasIndex(callback => new { callback.Provider, callback.ExternalCallbackId }).IsUnique();
        builder.HasIndex(callback => new { callback.Provider, callback.PayloadHash }).IsUnique();
        builder.HasIndex(callback => new { callback.IsVerified, callback.ReceivedAt });
        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(callback => callback.PaymentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
