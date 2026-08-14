using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoldInvoice.Application.Pricing;
using GoldInvoice.Domain.Pricing;

namespace GoldInvoice.Api.Market;

public sealed class NavasanMarketPriceProvider(
    IConfiguration configuration,
    ILogger<NavasanMarketPriceProvider> logger) : IMarketPriceProvider
{
    private const string DefaultBaseUrl = "http://api.navasan.tech/latest/";
    private static readonly HttpClient Client = new();

    public string ProviderCode => "NAVASAN";

    public async Task<IReadOnlyList<MarketPriceQuote>> FetchAsync(
        CancellationToken cancellationToken)
    {
        var apiKey = configuration["Navasan:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Navasan API key is not configured.");
        }

        var baseUrl = configuration["Navasan:BaseUrl"] ?? DefaultBaseUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException("Navasan:BaseUrl is invalid.");
        }

        var builder = new UriBuilder(baseUri);
        var existingQuery = builder.Query.TrimStart('?');
        builder.Query = string.IsNullOrWhiteSpace(existingQuery)
            ? $"api_key={Uri.EscapeDataString(apiKey.Trim())}"
            : $"{existingQuery}&api_key={Uri.EscapeDataString(apiKey.Trim())}";

        using var request = new HttpRequestMessage(HttpMethod.Get, builder.Uri);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("VendomeJewelry/1.0");

        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Navasan manual refresh failed with HTTP {StatusCode}.",
                (int)response.StatusCode);

            throw new HttpRequestException(
                $"Navasan returned HTTP {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Navasan response is not a JSON object.");
        }

        var gold18 = ReadRate(root, "18ayar");
        var usdReference = TryReadRate(root, "usd");
        var usdBuy = TryReadRate(root, "usd_buy") ?? usdReference
            ?? throw new InvalidDataException("Navasan USD buy rate is missing.");
        var usdSell = TryReadRate(root, "usd_sell") ?? usdReference
            ?? throw new InvalidDataException("Navasan USD sell rate is missing.");

        var gold18Rials = ToRials(gold18.Value);
        var gold24Rials = checked((gold18Rials * 4L + 1L) / 3L);

        var usdLow = Math.Min(usdBuy.Value, usdSell.Value);
        var usdHigh = Math.Max(usdBuy.Value, usdSell.Value);
        var usdBuyRials = ToRials(usdLow);
        var usdSellRials = ToRials(usdHigh);

        var usdTimestamp = Latest(usdBuy.Timestamp, usdSell.Timestamp);
        var usdIdentity =
            $"{usdBuy.RawValue}|{usdSell.RawValue}|{usdTimestamp?.ToUnixTimeSeconds() ?? 0}";

        return
        [
            new MarketPriceQuote(
                MarketPriceType.Gold18K,
                gold18Rials,
                gold18Rials,
                gold18.Timestamp,
                Hash("18ayar-v6", gold18.RawValue, gold18.Timestamp)),
            new MarketPriceQuote(
                MarketPriceType.Gold24K,
                gold24Rials,
                gold24Rials,
                gold18.Timestamp,
                Hash("24k-v6", gold18.RawValue, gold18.Timestamp)),
            new MarketPriceQuote(
                MarketPriceType.Currency,
                usdBuyRials,
                usdSellRials,
                null,
                Hash("currency-v6", usdIdentity, usdTimestamp))
        ];
    }

    private static ParsedRate ReadRate(JsonElement root, string symbol) =>
        TryReadRate(root, symbol)
        ?? throw new InvalidDataException($"Navasan symbol '{symbol}' is missing.");

    private static ParsedRate? TryReadRate(JsonElement root, string symbol)
    {
        if (!root.TryGetProperty(symbol, out var item) ||
            item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("value", out var valueElement))
        {
            return null;
        }

        var rawValue = valueElement.ValueKind == JsonValueKind.String
            ? valueElement.GetString() ?? string.Empty
            : valueElement.GetRawText();

        var normalized = rawValue
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (!long.TryParse(
                normalized,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value) ||
            value <= 0)
        {
            return null;
        }

        DateTimeOffset? timestamp = null;
        if (item.TryGetProperty("timestamp", out var timestampElement))
        {
            long unix;
            if (timestampElement.ValueKind == JsonValueKind.Number &&
                timestampElement.TryGetInt64(out unix))
            {
                timestamp = DateTimeOffset.FromUnixTimeSeconds(unix);
            }
            else if (timestampElement.ValueKind == JsonValueKind.String &&
                     long.TryParse(
                         timestampElement.GetString(),
                         NumberStyles.Integer,
                         CultureInfo.InvariantCulture,
                         out unix))
            {
                timestamp = DateTimeOffset.FromUnixTimeSeconds(unix);
            }
        }

        return new ParsedRate(value, rawValue, timestamp);
    }

    private static long ToRials(long tomans) => checked(tomans * 10L);

    private static DateTimeOffset? Latest(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null) return right;
        if (right is null) return left;
        return left >= right ? left : right;
    }

    private static string Hash(
        string symbol,
        string value,
        DateTimeOffset? timestamp)
    {
        var input =
            $"{symbol}|{value}|{timestamp?.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) ?? "none"}";
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    private sealed record ParsedRate(
        long Value,
        string RawValue,
        DateTimeOffset? Timestamp);
}
