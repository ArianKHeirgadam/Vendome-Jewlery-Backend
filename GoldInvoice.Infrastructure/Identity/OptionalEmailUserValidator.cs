using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace GoldInvoice.Infrastructure.Identity;

internal sealed class OptionalEmailUserValidator : IUserValidator<ApplicationUser>
{
    public async Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager,
        ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        var email = await manager.GetEmailAsync(user);
        if (string.IsNullOrWhiteSpace(email))
        {
            return IdentityResult.Success;
        }

        if (!new EmailAddressAttribute().IsValid(email))
        {
            return IdentityResult.Failed(manager.ErrorDescriber.InvalidEmail(email));
        }

        var existing = await manager.FindByEmailAsync(email);
        if (existing is not null && existing.Id != user.Id)
        {
            return IdentityResult.Failed(manager.ErrorDescriber.DuplicateEmail(email));
        }

        return IdentityResult.Success;
    }
}
