using GoldInvoice.Domain.Platform;
using GoldInvoice.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoldInvoice.Infrastructure.Persistence.Configurations;

internal sealed class DesktopDeviceConfiguration : IEntityTypeConfiguration<DesktopDevice>
{
    public void Configure(EntityTypeBuilder<DesktopDevice> builder)
    {
        builder.ToTable("DesktopDevices", DatabaseSchemas.Devices, table =>
        {
            table.HasCheckConstraint(
                "CK_DesktopDevices_Revocation",
                "([IsActive] = 1 AND [RevokedAt] IS NULL) OR ([IsActive] = 0 AND [RevokedAt] IS NOT NULL)");
        });
        builder.ConfigureAuditable();
        builder.Property(device => device.DeviceIdentifierHash).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(device => device.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(device => device.PublicKeyThumbprint).HasMaxLength(128).IsUnicode(false);
        builder.Property(device => device.IsActive).HasDefaultValue(true);
        builder.Property(device => device.LastSeenAt).HasPrecision(7);
        builder.Property(device => device.RevokedAt).HasPrecision(7);
        builder.HasIndex(device => device.DeviceIdentifierHash).IsUnique();
        builder.HasIndex(device => new { device.IsActive, device.LastSeenAt });
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(device => device.RegisteredByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages", DatabaseSchemas.Integration, table =>
        {
            table.HasCheckConstraint("CK_OutboxMessages_RetryCount", "[RetryCount] >= 0");
            table.HasCheckConstraint(
                "CK_OutboxMessages_Status",
                "[Status] IN ('Pending', 'Processing', 'Processed', 'Failed', 'DeadLetter')");
            table.HasCheckConstraint(
                "CK_OutboxMessages_Processing",
                "([Status] = 'Processing' AND [LockId] IS NOT NULL AND [LockedUntil] IS NOT NULL) OR [Status] <> 'Processing'");
        });
        builder.ConfigureAuditable();
        builder.Property(message => message.MessageType).HasMaxLength(300).IsUnicode(false).IsRequired();
        builder.Property(message => message.Payload).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(message => message.OccurredAt).HasPrecision(7);
        builder.Property(message => message.ProcessedAt).HasPrecision(7);
        builder.Property(message => message.NextRetryAt).HasPrecision(7);
        builder.Property(message => message.LastError).HasMaxLength(4000);
        builder.Property(message => message.Status).ConfigureEnum();
        builder.Property(message => message.LockedUntil).HasPrecision(7);
        builder.HasIndex(message => new { message.Status, message.NextRetryAt, message.OccurredAt });
        builder.HasIndex(message => new { message.LockId, message.LockedUntil })
            .HasFilter("[LockId] IS NOT NULL");
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs", DatabaseSchemas.Audit);
        builder.ConfigureAuditable();
        builder.Property(log => log.Action).HasMaxLength(200).IsUnicode(false).IsRequired();
        builder.Property(log => log.EntityType).HasMaxLength(300).IsUnicode(false).IsRequired();
        builder.Property(log => log.EntityId).HasMaxLength(200).IsUnicode(false).IsRequired();
        builder.Property(log => log.OccurredAt).HasPrecision(7);
        builder.Property(log => log.CorrelationId).HasMaxLength(128).IsUnicode(false);
        builder.Property(log => log.IpAddress).HasMaxLength(64).IsUnicode(false);
        builder.Property(log => log.OldValuesJson).HasColumnType("nvarchar(max)");
        builder.Property(log => log.NewValuesJson).HasColumnType("nvarchar(max)");
        builder.HasIndex(log => new { log.EntityType, log.EntityId, log.OccurredAt });
        builder.HasIndex(log => new { log.ActorUserId, log.OccurredAt });
        builder.HasIndex(log => log.CorrelationId);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(log => log.ActorUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("SystemSettings", DatabaseSchemas.Configuration, table =>
        {
            table.HasCheckConstraint(
                "CK_SystemSettings_ValueSource",
                "([Value] IS NOT NULL AND [SecretReference] IS NULL) OR ([Value] IS NULL AND [SecretReference] IS NOT NULL)");
        });
        builder.ConfigureAuditable();
        builder.Property(setting => setting.Key).HasMaxLength(200).IsUnicode(false).IsRequired();
        builder.Property(setting => setting.DataType).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(setting => setting.Value).HasMaxLength(4000);
        builder.Property(setting => setting.SecretReference).HasMaxLength(500).IsUnicode(false);
        builder.Property(setting => setting.Description).HasMaxLength(1000);
        builder.Property(setting => setting.IsReadOnly).HasDefaultValue(false);
        builder.HasIndex(setting => setting.Key).IsUnique();
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords", DatabaseSchemas.Platform, table =>
        {
            table.HasCheckConstraint(
                "CK_IdempotencyRecords_Status",
                "[Status] IN ('Processing', 'Completed', 'Failed')");
            table.HasCheckConstraint("CK_IdempotencyRecords_Expiry", "[ExpiresAt] > [CreatedAt]");
            table.HasCheckConstraint(
                "CK_IdempotencyRecords_ResponseCode",
                "[ResponseStatusCode] IS NULL OR [ResponseStatusCode] BETWEEN 100 AND 599");
        });
        builder.ConfigureAuditable();
        builder.Property(record => record.Scope).HasMaxLength(200).IsUnicode(false).IsRequired();
        builder.Property(record => record.KeyHash).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(record => record.RequestHash).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(record => record.Status).ConfigureEnum();
        builder.Property(record => record.ResponseBody).HasColumnType("nvarchar(max)");
        builder.Property(record => record.CompletedAt).HasPrecision(7);
        builder.Property(record => record.ExpiresAt).HasPrecision(7);
        builder.Property(record => record.LockedUntil).HasPrecision(7);
        builder.HasIndex(record => new { record.Scope, record.KeyHash }).IsUnique();
        builder.HasIndex(record => new { record.Status, record.ExpiresAt });
    }
}
