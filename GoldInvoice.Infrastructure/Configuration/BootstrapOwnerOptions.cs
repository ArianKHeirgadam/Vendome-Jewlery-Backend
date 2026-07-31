namespace GoldInvoice.Infrastructure.Configuration;

public sealed class BootstrapOwnerOptions
{
    public const string SectionName = "Security:BootstrapOwner";

    public bool Enabled { get; init; }

    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public static bool IsValid(BootstrapOwnerOptions options)
    {
        if (!options.Enabled)
        {
            return string.IsNullOrEmpty(options.Email) &&
                string.IsNullOrEmpty(options.Password) &&
                string.IsNullOrEmpty(options.DisplayName);
        }

        return !string.IsNullOrWhiteSpace(options.Email) &&
            options.Email.Length <= 320 &&
            !string.IsNullOrWhiteSpace(options.Password) &&
            options.Password.Length <= 256 &&
            !string.IsNullOrWhiteSpace(options.DisplayName) &&
            options.DisplayName.Length <= 200;
    }
}
