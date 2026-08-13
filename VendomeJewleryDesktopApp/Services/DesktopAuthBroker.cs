using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VendomeJewleryDesktopApp.Services;

internal sealed class DesktopAuthBroker : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly DesktopSettingsStore _settingsStore;
    private readonly DesktopSecretStore _secretStore;
    private readonly HttpClient _httpClient;

    public DesktopAuthBroker(
        DesktopSettingsStore settingsStore,
        DesktopSecretStore secretStore)
    {
        _settingsStore = settingsStore;
        _secretStore = secretStore;
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = System.Net.DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
    }

    public Task<JsonElement> LoginAsync(JsonElement payload, CancellationToken cancellationToken) =>
        SendAsync("/api/v1/auth/login", payload, captureRefreshToken: true, cancellationToken: cancellationToken);

    public async Task<JsonElement> RefreshAsync(CancellationToken cancellationToken)
    {
        string refreshToken = _secretStore.ReadRefreshToken() ??
            throw new DesktopBridgeException(
                "session_unavailable",
                "نشست ذخیره‌شده‌ای وجود ندارد.",
                401);
        JsonElement payload = JsonSerializer.SerializeToElement(
            new { refreshToken },
            JsonOptions);
        return await SendAsync(
            "/api/v1/auth/refresh",
            payload,
            captureRefreshToken: true,
            cancellationToken: cancellationToken);
    }

    public Task<JsonElement> SetupMfaAsync(JsonElement payload, CancellationToken cancellationToken) =>
        SendAsync("/api/v1/auth/mfa/setup", payload, captureRefreshToken: false, cancellationToken: cancellationToken);

    public Task<JsonElement> EnableMfaAsync(JsonElement payload, CancellationToken cancellationToken) =>
        SendAsync("/api/v1/auth/mfa/enable", payload, captureRefreshToken: true, cancellationToken: cancellationToken);

    public async Task<JsonElement> LogoutAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        string? accessToken = payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("accessToken", out JsonElement tokenElement)
                ? tokenElement.GetString()
                : null;

        try
        {
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                await SendAsync(
                    "/api/v1/auth/logout",
                    JsonSerializer.SerializeToElement(new { }, JsonOptions),
                    captureRefreshToken: false,
                    cancellationToken: cancellationToken,
                    accessToken: accessToken);
            }
        }
        finally
        {
            _secretStore.Clear();
        }

        return JsonSerializer.SerializeToElement(new { cleared = true }, JsonOptions);
    }

    public void ClearSession() => _secretStore.Clear();

    public void Dispose() => _httpClient.Dispose();

    private async Task<JsonElement> SendAsync(
        string path,
        JsonElement payload,
        bool captureRefreshToken,
        CancellationToken cancellationToken,
        string? accessToken = null)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(new Uri(_settingsStore.ApiBaseUrl + "/", UriKind.Absolute), path.TrimStart('/')));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            request.Content = new StringContent(
                payload.GetRawText(),
                Encoding.UTF8,
                "application/json");

            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiFailure((int)response.StatusCode, responseBody);
            }

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return JsonSerializer.SerializeToElement(new { }, JsonOptions);
            }

            JsonNode responseNode = JsonNode.Parse(responseBody) ??
                throw new DesktopBridgeException(
                    "invalid_api_response",
                    "پاسخ احراز هویت سرور معتبر نیست.");
            if (captureRefreshToken)
            {
                CaptureAndRemoveRefreshToken(responseNode);
            }

            return JsonSerializer.SerializeToElement(responseNode, JsonOptions);
        }
        catch (DesktopBridgeException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DesktopBridgeException(
                "api_timeout",
                "پاسخ API بیش از حد طول کشید. وضعیت سرور را بررسی کن.",
                504);
        }
        catch (HttpRequestException)
        {
            throw new DesktopBridgeException(
                "api_unreachable",
                "ارتباط با API برقرار نشد. آدرس و گواهی سرور را بررسی کن.",
                503);
        }
        catch (JsonException)
        {
            throw new DesktopBridgeException(
                "invalid_api_response",
                "پاسخ احراز هویت سرور معتبر نیست.",
                502);
        }
    }

    private void CaptureAndRemoveRefreshToken(JsonNode responseNode)
    {
        JsonObject? tokenObject = responseNode as JsonObject;
        if (responseNode is JsonObject root && root["tokens"] is JsonObject nestedTokens)
        {
            tokenObject = nestedTokens;
        }

        bool containsAccessToken = tokenObject?["accessToken"] is JsonValue;
        if (tokenObject is null || tokenObject["refreshToken"] is not JsonValue tokenValue ||
            !tokenValue.TryGetValue(out string? refreshToken) ||
            string.IsNullOrWhiteSpace(refreshToken))
        {
            if (containsAccessToken)
            {
                throw new DesktopBridgeException(
                    "invalid_api_response",
                    "سرور توکن تازه‌سازی نشست را برنگرداند.",
                    502);
            }

            return;
        }

        _secretStore.SaveRefreshToken(refreshToken);
        tokenObject.Remove("refreshToken");
    }

    private static DesktopBridgeException CreateApiFailure(int status, string responseBody)
    {
        string? detail = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(responseBody) &&
                JsonNode.Parse(responseBody) is JsonObject problem)
            {
                detail = problem["detail"]?.GetValue<string>() ??
                    problem["title"]?.GetValue<string>();
            }
        }
        catch (JsonException)
        {
            // Use the generic safe message below.
        }

        string message = !string.IsNullOrWhiteSpace(detail) && detail.Length <= 500
            ? detail
            : status switch
            {
                401 => "اطلاعات ورود یا نشست معتبر نیست.",
                403 => "این عملیات برای حساب فعلی مجاز نیست.",
                429 => "تعداد تلاش‌ها زیاد بوده است؛ کمی بعد دوباره امتحان کن.",
                >= 500 => "API نتوانست درخواست را کامل کند.",
                _ => "درخواست احراز هویت معتبر نبود."
            };
        return new DesktopBridgeException("api_error", message, status);
    }
}
