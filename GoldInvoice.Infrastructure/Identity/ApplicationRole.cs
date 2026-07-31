using GoldInvoice.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace GoldInvoice.Infrastructure.Identity;

public sealed class ApplicationRole : IdentityRole<Guid>, IAuditableEntity
{
    public ApplicationRole()
    {
        Id = Guid.NewGuid();
        ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }

    public ApplicationRole(string name, string description, bool isSystem = false)
        : this()
    {
        Name = name;
        NormalizedName = name.ToUpperInvariant();
        Description = description;
        IsSystem = isSystem;
    }

    public string Description { get; private set; } = string.Empty;

    public bool IsSystem { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public byte[] RowVersion { get; private set; } = [];
}
