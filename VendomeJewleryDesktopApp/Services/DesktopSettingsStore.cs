using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace VendomeJewleryDesktopApp.Services;

internal sealed record DesktopRuntimeConfiguration(
    string ApiBaseUrl,
    bool IsDesktop,
    bool HasRefreshToken,
    bool IsInsecureTransport);

internal sealed class DesktopSettingsStore
{
    public const string DefaultApiBaseUrl = "https://localhost:7156";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public DesktopSettingsStore(string applicationDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);
        Directory.CreateDirectory(applicationDataDirectory);
        _settingsPath = Path.Combine(applicationDataDirectory, "desktop-settings.json");
        ApiBaseUrl = LoadApiBaseUrl();
    }

    public string ApiBaseUrl { get; private set; }

    public bool Configure(string apiBaseUrl)
    {
        string normalized = ValidateAndNormalize(apiBaseUrl);
        bool changed = !string.Equals(ApiBaseUrl, normalized, StringComparison.OrdinalIgnoreCase);
        ApiBaseUrl = normalized;

        string json = JsonSerializer.Serialize(
            new DesktopSettingsDocument { ApiBaseUrl = ApiBaseUrl },
            JsonOptions);
        string temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _settingsPath, overwrite: true);
        return changed;
    }

    public DesktopRuntimeConfiguration GetRuntimeConfiguration(bool hasRefreshToken)
    {
        var uri = new Uri(ApiBaseUrl, UriKind.Absolute);
        return new DesktopRuntimeConfiguration(
            ApiBaseUrl,
            IsDesktop: true,
            hasRefreshToken,
            IsInsecureTransport: uri.Scheme == Uri.UriSchemeHttp);
    }

    private string LoadApiBaseUrl()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return DefaultApiBaseUrl;
            }

            var document = JsonSerializer.Deserialize<DesktopSettingsDocument>(
                File.ReadAllText(_settingsPath),
                JsonOptions);
            return ValidateAndNormalize(document?.ApiBaseUrl ?? DefaultApiBaseUrl);
        }
        catch (JsonException)
        {
            return DefaultApiBaseUrl;
        }
        catch (IOException)
        {
            return DefaultApiBaseUrl;
        }
        catch (DesktopBridgeException)
        {
            return DefaultApiBaseUrl;
        }
    }

    private static string ValidateAndNormalize(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/")
        {
            throw new DesktopBridgeException(
                "invalid_api_url",
                "آدرس API باید فقط شامل پروتکل، نام سرور و پورت باشد.",
                400);
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !IsLocalDevelopmentHost(uri.Host))
        {
            throw new DesktopBridgeException(
                "insecure_api_url",
                "برای سرور خارج از شبکهٔ خصوصی باید از HTTPS استفاده شود.",
                400);
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    private static bool IsLocalDevelopmentHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out IPAddress? address))
        {
            return host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168) ||
                (bytes[0] == 169 && bytes[1] == 254);
        }

        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal;
    }

    private sealed class DesktopSettingsDocument
    {
        public string ApiBaseUrl { get; init; } = DefaultApiBaseUrl;
    }
}
