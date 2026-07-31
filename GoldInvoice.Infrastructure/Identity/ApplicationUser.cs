using GoldInvoice.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace GoldInvoice.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>, IAuditableEntity
{
    public ApplicationUser()
    {
        Id = Guid.NewGuid();
        SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public ApplicationUser(string displayName)
        : this()
    {
        SetDisplayName(displayName);
    }

    public string DisplayName { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    public bool MfaRequired { get; private set; }

    public DateTimeOffset? DeactivatedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public void SetDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 200)
        {
            throw new ArgumentException("A display name of at most 200 characters is required.", nameof(displayName));
        }

        DisplayName = displayName.Trim();
    }

    public void RequireMfa() => MfaRequired = true;

    public void Deactivate(DateTimeOffset deactivatedAt)
    {
        if (deactivatedAt == default)
        {
            throw new ArgumentException("A deactivation time is required.", nameof(deactivatedAt));
        }

        IsActive = false;
        DeactivatedAt = deactivatedAt;
    }

    public void Reactivate()
    {
        IsActive = true;
        DeactivatedAt = null;
    }
}
