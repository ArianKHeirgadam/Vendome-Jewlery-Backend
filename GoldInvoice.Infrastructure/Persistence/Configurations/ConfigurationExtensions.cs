using GoldInvoice.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoldInvoice.Infrastructure.Persistence.Configurations;

internal static class DatabaseSchemas
{
    public const string Security = "security";
    public const string Catalog = "catalog";
    public const string Pricing = "pricing";
    public const string Inventory = "inventory";
    public const string Sales = "sales";
    public const string Billing = "billing";
    public const string Invoicing = "invoicing";
    public const string Devices = "devices";
    public const string Integration = "integration";
    public const string Audit = "audit";
    public const string Configuration = "configuration";
    public const string Platform = "platform";
    public const string Business = "business";
    public const string Crm = "crm";
}

internal static class ConfigurationExtensions
{
    public static void ConfigureAuditable<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : AuditableEntity
    {
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.CreatedAt).HasPrecision(7).IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasPrecision(7).IsRequired();
        builder.Property(entity => entity.RowVersion).IsRowVersion();
    }

    public static void ConfigureSoftDelete<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : SoftDeletableEntity
    {
        builder.ConfigureAuditable();
        builder.Property(entity => entity.IsDeleted).HasDefaultValue(false);
        builder.Property(entity => entity.DeletedAt).HasPrecision(7);
        builder.HasQueryFilter(entity => !entity.IsDeleted);
    }

    public static PropertyBuilder<TEnum> ConfigureEnum<TEnum>(
        this PropertyBuilder<TEnum> property,
        int maximumLength = 50)
        where TEnum : struct, Enum =>
        property
            .HasConversion<string>()
            .HasMaxLength(maximumLength)
            .IsUnicode(false);

    public static PropertyBuilder<TEnum?> ConfigureNullableEnum<TEnum>(
        this PropertyBuilder<TEnum?> property,
        int maximumLength = 50)
        where TEnum : struct, Enum =>
        property
            .HasConversion<string>()
            .HasMaxLength(maximumLength)
            .IsUnicode(false);
}
