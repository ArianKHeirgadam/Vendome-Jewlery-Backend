using GoldInvoice.Domain.Security;
using GoldInvoice.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoldInvoice.Infrastructure.Persistence.Configurations;

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions", DatabaseSchemas.Security);
        builder.ConfigureAuditable();
        builder.Property(permission => permission.Name).HasMaxLength(150).IsUnicode(false).IsRequired();
        builder.Property(permission => permission.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(permission => permission.Group).HasMaxLength(100).IsRequired();
        builder.Property(permission => permission.Description).HasMaxLength(500);
        builder.Property(permission => permission.IsActive).HasDefaultValue(true);
        builder.HasIndex(permission => permission.Name).IsUnique();
        builder.HasIndex(permission => new { permission.Group, permission.IsActive });
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions", DatabaseSchemas.Security);
        builder.HasKey(rolePermission => new { rolePermission.RoleId, rolePermission.PermissionId });
        builder.Property(rolePermission => rolePermission.GrantedAt).HasPrecision(7).IsRequired();
        builder.Property(rolePermission => rolePermission.CreatedAt).HasPrecision(7).IsRequired();
        builder.Property(rolePermission => rolePermission.UpdatedAt).HasPrecision(7).IsRequired();
        builder.Property(rolePermission => rolePermission.RowVersion).IsRowVersion();
        builder.HasOne<ApplicationRole>()
            .WithMany()
            .HasForeignKey(rolePermission => rolePermission.RoleId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Permission>()
            .WithMany()
            .HasForeignKey(rolePermission => rolePermission.PermissionId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(rolePermission => rolePermission.GrantedBy)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(rolePermission => rolePermission.PermissionId);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens", DatabaseSchemas.Security, table =>
        {
            table.HasCheckConstraint("CK_RefreshTokens_Expiry", "[ExpiresAt] > [CreatedAt]");
            table.HasCheckConstraint(
                "CK_RefreshTokens_Lifecycle",
                "([UsedAt] IS NULL OR [UsedAt] >= [CreatedAt]) AND ([RevokedAt] IS NULL OR [RevokedAt] >= [CreatedAt])");
        });
        builder.ConfigureAuditable();
        builder.Property(token => token.TokenHash).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(token => token.ExpiresAt).HasPrecision(7);
        builder.Property(token => token.UsedAt).HasPrecision(7);
        builder.Property(token => token.RevokedAt).HasPrecision(7);
        builder.Property(token => token.RevocationReason).HasMaxLength(500);
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => new { token.SessionId, token.RevokedAt, token.ExpiresAt });
        builder.HasIndex(token => token.FamilyId);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<UserSession>()
            .WithMany()
            .HasForeignKey(token => token.SessionId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<RefreshToken>()
            .WithMany()
            .HasForeignKey(token => token.ParentTokenId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<RefreshToken>()
            .WithMany()
            .HasForeignKey(token => token.ReplacedByTokenId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("UserSessions", DatabaseSchemas.Security, table =>
        {
            table.HasCheckConstraint("CK_UserSessions_Expiry", "[ExpiresAt] > [CreatedAt]");
            table.HasCheckConstraint(
                "CK_UserSessions_Revocation",
                "[RevokedAt] IS NULL OR [RevokedAt] >= [CreatedAt]");
        });
        builder.ConfigureAuditable();
        builder.Property(session => session.ExpiresAt).HasPrecision(7);
        builder.Property(session => session.LastSeenAt).HasPrecision(7);
        builder.Property(session => session.RevokedAt).HasPrecision(7);
        builder.Property(session => session.RevocationReason).HasMaxLength(500);
        builder.Property(session => session.SecurityStamp).HasMaxLength(256).IsUnicode(false).IsRequired();
        builder.Property(session => session.IpAddress).HasMaxLength(64).IsUnicode(false);
        builder.Property(session => session.UserAgentHash).HasMaxLength(128).IsUnicode(false);
        builder.HasIndex(session => new { session.UserId, session.RevokedAt, session.ExpiresAt });
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<TrustedDevice>()
            .WithMany()
            .HasForeignKey(session => session.TrustedDeviceId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class TrustedDeviceConfiguration : IEntityTypeConfiguration<TrustedDevice>
{
    public void Configure(EntityTypeBuilder<TrustedDevice> builder)
    {
        builder.ToTable("TrustedDevices", DatabaseSchemas.Security, table =>
        {
            table.HasCheckConstraint("CK_TrustedDevices_Expiry", "[TrustExpiresAt] > [CreatedAt]");
        });
        builder.ConfigureAuditable();
        builder.Property(device => device.DeviceIdentifierHash).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(device => device.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(device => device.TrustExpiresAt).HasPrecision(7);
        builder.Property(device => device.LastUsedAt).HasPrecision(7);
        builder.Property(device => device.RevokedAt).HasPrecision(7);
        builder.HasIndex(device => new { device.UserId, device.DeviceIdentifierHash }).IsUnique();
        builder.HasIndex(device => new { device.UserId, device.RevokedAt, device.TrustExpiresAt });
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(device => device.UserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class LoginAttemptConfiguration : IEntityTypeConfiguration<LoginAttempt>
{
    public void Configure(EntityTypeBuilder<LoginAttempt> builder)
    {
        builder.ToTable("LoginAttempts", DatabaseSchemas.Security);
        builder.ConfigureAuditable();
        builder.Property(attempt => attempt.NormalizedIdentifierHash).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(attempt => attempt.FailureReason).HasMaxLength(200);
        builder.Property(attempt => attempt.IpAddress).HasMaxLength(64).IsUnicode(false);
        builder.Property(attempt => attempt.UserAgentHash).HasMaxLength(128).IsUnicode(false);
        builder.Property(attempt => attempt.OccurredAt).HasPrecision(7);
        builder.HasIndex(attempt => new { attempt.NormalizedIdentifierHash, attempt.OccurredAt });
        builder.HasIndex(attempt => new { attempt.IpAddress, attempt.OccurredAt });
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(attempt => attempt.UserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEvent>
{
    public void Configure(EntityTypeBuilder<SecurityEvent> builder)
    {
        builder.ToTable("SecurityEvents", DatabaseSchemas.Security, table =>
        {
            table.HasCheckConstraint(
                "CK_SecurityEvents_Severity",
                "[Severity] IN ('Information', 'Warning', 'Critical')");
        });
        builder.ConfigureAuditable();
        builder.Property(securityEvent => securityEvent.EventType).HasMaxLength(150).IsUnicode(false).IsRequired();
        builder.Property(securityEvent => securityEvent.Severity).ConfigureEnum();
        builder.Property(securityEvent => securityEvent.OccurredAt).HasPrecision(7);
        builder.Property(securityEvent => securityEvent.CorrelationId).HasMaxLength(128).IsUnicode(false);
        builder.Property(securityEvent => securityEvent.IpAddress).HasMaxLength(64).IsUnicode(false);
        builder.Property(securityEvent => securityEvent.DetailsJson).HasColumnType("nvarchar(max)");
        builder.HasIndex(securityEvent => new { securityEvent.UserId, securityEvent.OccurredAt });
        builder.HasIndex(securityEvent => new { securityEvent.Severity, securityEvent.OccurredAt });
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(securityEvent => securityEvent.UserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<UserSession>()
            .WithMany()
            .HasForeignKey(securityEvent => securityEvent.SessionId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
