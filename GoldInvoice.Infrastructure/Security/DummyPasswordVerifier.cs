using GoldInvoice.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GoldInvoice.Infrastructure.Security;

internal interface IDummyPasswordVerifier
{
    void Verify(string password);
}

internal sealed class DummyPasswordVerifier : IDummyPasswordVerifier
{
    private readonly ApplicationUser dummyUser = new("Unavailable account");
    private readonly PasswordHasher<ApplicationUser> passwordHasher;
    private readonly string passwordHash;

    public DummyPasswordVerifier(IOptions<PasswordHasherOptions> options)
    {
        passwordHasher = new PasswordHasher<ApplicationUser>(options);
        passwordHash = passwordHasher.HashPassword(dummyUser, Guid.NewGuid().ToString("N"));
    }

    public void Verify(string password) =>
        _ = passwordHasher.VerifyHashedPassword(dummyUser, passwordHash, password);
}
