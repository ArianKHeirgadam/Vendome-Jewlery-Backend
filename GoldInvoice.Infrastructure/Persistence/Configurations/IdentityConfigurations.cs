using GoldInvoice.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoldInvoice.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("Users", DatabaseSchemas.Security, table =>
        {
            table.HasCheckConstraint(
                "CK_Users_Deactivation",
                "([IsActive] = 1 AND [DeactivatedAt] IS NULL) OR ([IsActive] = 0 AND [DeactivatedAt] IS NOT NULL)");
        });
        builder.Property(user => user.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(user => user.IsActive).HasDefaultValue(true);
        builder.Property(user => user.MfaRequired).HasDefaultValue(false);
        builder.Property(user => user.DeactivatedAt).HasPrecision(7);
        builder.Property(user => user.CreatedAt).HasPrecision(7).IsRequired();
        builder.Property(user => user.UpdatedAt).HasPrecision(7).IsRequired();
        builder.Property(user => user.RowVersion).IsRowVersion();

        builder.HasMany<IdentityUserClaim<Guid>>()
            .WithOne()
            .HasForeignKey(claim => claim.UserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasMany<IdentityUserLogin<Guid>>()
            .WithOne()
            .HasForeignKey(login => login.UserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasMany<IdentityUserToken<Guid>>()
            .WithOne()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasMany<IdentityUserRole<Guid>>()
            .WithOne()
            .HasForeignKey(userRole => userRole.UserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationRole> builder)
    {
        builder.ToTable("Roles", DatabaseSchemas.Security);
        builder.Property(role => role.Description).HasMaxLength(500).IsRequired();
        builder.Property(role => role.IsSystem).HasDefaultValue(false);
        builder.Property(role => role.CreatedAt).HasPrecision(7).IsRequired();
        builder.Property(role => role.UpdatedAt).HasPrecision(7).IsRequired();
        builder.Property(role => role.RowVersion).IsRowVersion();

        builder.HasMany<IdentityRoleClaim<Guid>>()
            .WithOne()
            .HasForeignKey(claim => claim.RoleId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasMany<IdentityUserRole<Guid>>()
            .WithOne()
            .HasForeignKey(userRole => userRole.RoleId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class IdentityUserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder) =>
        builder.ToTable("UserClaims", DatabaseSchemas.Security);
}

internal sealed class IdentityUserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder) =>
        builder.ToTable("UserLogins", DatabaseSchemas.Security);
}

internal sealed class IdentityUserRoleConfiguration : IEntityTypeConfiguration<IdentityUserRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<Guid>> builder) =>
        builder.ToTable("UserRoles", DatabaseSchemas.Security);
}

internal sealed class IdentityUserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder) =>
        builder.ToTable("UserTokens", DatabaseSchemas.Security);
}

internal sealed class IdentityRoleClaimConfiguration : IEntityTypeConfiguration<IdentityRoleClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<Guid>> builder) =>
        builder.ToTable("RoleClaims", DatabaseSchemas.Security);
}
