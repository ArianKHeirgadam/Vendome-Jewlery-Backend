using System.Management;
using System.Runtime.InteropServices;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace VendomeJewleryDesktopApp.Services;

internal sealed class LocalDeviceDetectionAgent : IDisposable
{
    private readonly Func<string?> _accessTokenProvider;
    private readonly Func<string> _apiBaseUrlProvider;
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public LocalDeviceDetectionAgent(Func<string?> accessTokenProvider, Func<string> apiBaseUrlProvider)
    {
        _accessTokenProvider = accessTokenProvider;
        _apiBaseUrlProvider = apiBaseUrlProvider;
    }

    public void Start() => _loop ??= Task.Run(RunAsync);

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        await SynchronizeOnceSafeAsync(_cts.Token);
        while (await timer.WaitForNextTickAsync(_cts.Token))
            await SynchronizeOnceSafeAsync(_cts.Token);
    }

    private async Task SynchronizeOnceSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var token = _accessTokenProvider();
            var apiBaseUrl = _apiBaseUrlProvider();
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(apiBaseUrl)) return;

            var devices = DiscoverPrinters().Concat(DiscoverScanners())
                .GroupBy(x => x.Identifier, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First()).Take(100).ToArray();

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(new Uri(apiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute), "api/v1/devices/sync"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(
                JsonSerializer.Serialize(devices.Select(x => new { x.Identifier, x.DisplayName, x.Model, Type = x.Type.ToString() })),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            _ = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // Device polling must never terminate the desktop host.
        }
    }

    private static IEnumerable<DetectedDevice> DiscoverPrinters()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DeviceID, DriverName, PortName FROM Win32_Printer");

        foreach (ManagementObject printer in searcher.Get())
        {
            var name = printer["Name"]?.ToString();
            var deviceId = printer["DeviceID"]?.ToString();
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(deviceId)) continue;

            yield return new DetectedDevice(
                "printer|" + (deviceId ?? name),
                name ?? deviceId!,
                printer["DriverName"]?.ToString() ?? printer["PortName"]?.ToString(),
                Domain.Platform.DeviceType.Printer);
        }
    }

    private static IEnumerable<DetectedDevice> DiscoverScanners()
    {
        var managerType = Type.GetTypeFromProgID("WIA.DeviceManager", false);
        if (managerType is null) yield break;

        object? manager = null;
        try
        {
            manager = Activator.CreateInstance(managerType);
            if (manager is null) yield break;

            dynamic infos = ((dynamic)manager).DeviceInfos;
            var count = Convert.ToInt32(infos.Count);
            for (var i = 1; i <= count; i++)
            {
                dynamic info = infos.Item(i);
                string? id = TryWiaString(info, "DeviceID");
                string? name = TryWiaString(info, "Name") ?? TryWiaString(info, "Description");
                if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name)) continue;

                yield return new DetectedDevice(
                    "scanner|" + (id ?? name ?? $"wia-{i}"),
                    name ?? id!,
                    TryWiaString(info, "Model") ?? TryWiaString(info, "Manufacturer"),
                    Domain.Platform.DeviceType.Scanner);
            }
        }
        catch (COMException)
        {
            yield break;
        }
        catch
        {
            yield break;
        }
        finally
        {
            if (manager is not null && Marshal.IsComObject(manager))
            {
                try { Marshal.FinalReleaseComObject(manager); } catch { }
            }
        }
    }

    private static string? TryWiaString(dynamic info, string propertyName)
    {
        try { return info.Properties.Item(propertyName).Value?.ToString(); }
        catch { return null; }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _httpClient.Dispose();
        _cts.Dispose();
    }

    private sealed record DetectedDevice(
        string Identifier,
        string DisplayName,
        string? Model,
        Domain.Platform.DeviceType Type);
}
