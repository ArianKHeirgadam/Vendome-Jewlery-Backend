using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VendomeJewleryDesktopApp.Services;

internal sealed class DesktopSecretStore
{
    private static readonly byte[] OptionalEntropy =
        Encoding.UTF8.GetBytes("VendomeJewelry.Desktop.Authentication.v1");

    private readonly string _secretPath;

    public DesktopSecretStore(string applicationDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);
        string secretDirectory = Path.Combine(applicationDataDirectory, "Secrets");
        Directory.CreateDirectory(secretDirectory);
        _secretPath = Path.Combine(secretDirectory, "refresh-token.bin");
    }

    public bool Exists => File.Exists(_secretPath);

    public string? ReadRefreshToken()
    {
        if (!File.Exists(_secretPath))
        {
            return null;
        }

        try
        {
            byte[] protectedBytes = File.ReadAllBytes(_secretPath);
            byte[] clearBytes = ProtectedData.Unprotect(
                protectedBytes,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);
            string token = Encoding.UTF8.GetString(clearBytes);
            CryptographicOperations.ZeroMemory(clearBytes);
            return token.Length is >= 32 and <= 8192 ? token : null;
        }
        catch (CryptographicException)
        {
            Clear();
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void SaveRefreshToken(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length is < 32 or > 8192)
        {
            throw new DesktopBridgeException(
                "invalid_refresh_token",
                "توکن نشست دریافتی از سرور معتبر نیست.");
        }

        byte[] clearBytes = Encoding.UTF8.GetBytes(refreshToken);
        try
        {
            byte[] protectedBytes = ProtectedData.Protect(
                clearBytes,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);
            string temporaryPath = _secretPath + ".tmp";
            File.WriteAllBytes(temporaryPath, protectedBytes);
            File.Move(temporaryPath, _secretPath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_secretPath))
            {
                File.Delete(_secretPath);
            }
        }
        catch (IOException)
        {
            // A later refresh will fail safely if Windows still has the file locked.
        }
        catch (UnauthorizedAccessException)
        {
            // Never expose filesystem details to the React layer.
        }
    }
}
