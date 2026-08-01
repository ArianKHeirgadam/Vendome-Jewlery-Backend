using GoldInvoice.Domain.Customers;
using GoldInvoice.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoldInvoice.Infrastructure.Persistence.Configurations;

internal sealed class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> builder)
    {
        builder.ToTable("CustomerAddresses", DatabaseSchemas.Sales, table =>
        {
            table.HasCheckConstraint(
                "CK_CustomerAddresses_SoftDelete",
                "([IsDeleted] = 0 AND [DeletedAt] IS NULL) OR ([IsDeleted] = 1 AND [DeletedAt] IS NOT NULL)");
        });
        builder.ConfigureSoftDelete();
        builder.Property(address => address.Title).HasMaxLength(100).IsRequired();
        builder.Property(address => address.RecipientName).HasMaxLength(200).IsRequired();
        builder.Property(address => address.PhoneNumber).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(address => address.Province).HasMaxLength(100).IsRequired();
        builder.Property(address => address.City).HasMaxLength(100).IsRequired();
        builder.Property(address => address.PostalCode).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(address => address.AddressLine).HasMaxLength(1000).IsRequired();
        builder.Property(address => address.IsDefault).HasDefaultValue(false);
        builder.HasIndex(address => new { address.CustomerId, address.CreatedAt });
        builder.HasIndex(address => address.CustomerId)
            .IsUnique()
            .HasFilter("[IsDefault] = 1 AND [IsDeleted] = 0");
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(address => address.CustomerId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
