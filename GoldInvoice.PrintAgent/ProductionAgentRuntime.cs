using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GoldInvoice.PrintAgent;

internal static class ProductionAgentRuntime
{
    private static Mutex? _singleInstance;
    private static CancellationTokenSource? _shutdown;

    [ModuleInitializer]
    internal static void Initialize()
    {
        try
        {
            _singleInstance = new Mutex(true, $"Local\\VendomeJewelry.PrintAgent.{Environment.UserName}", out var createdNew);
            if (!createdNew)
            {
                Environment.ExitCode = 16;
                Environment.Exit(16);
                return;
            }

            _shutdown = new CancellationTokenSource();
            _ = HeartbeatLoopAsync(_shutdown.Token);
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { _shutdown.Cancel(); } catch { }
                try { _singleInstance.ReleaseMutex(); } catch { }
                _singleInstance.Dispose();
            };
        }
        catch
        {
            // Startup hardening must never prevent enrollment or normal agent startup.
        }
    }

    private static async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var state = AgentStateStore.LoadOrCreate().LoadRequired();
                if (state is not null)
                {
                    await SendHeartbeatAsync(state, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException or JsonException or CryptographicException)
            {
                Console.Error.WriteLine($"Heartbeat failed: {ex.Message}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private static async Task SendHeartbeatAsync(AgentState state, CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var payload = $"heartbeat|{state.DeviceId:N}|{timestamp:o}";
        using var rsa = RSA.Create();
        rsa.ImportFromPem(state.PrivateKeyPem);
        var signature = Convert.ToBase64String(
            rsa.SignData(
                Encoding.UTF8.GetBytes(payload),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1));

        using var client = new HttpClient
        {
            BaseAddress = new Uri(state.Server.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(10)
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"api/v1/devices/{state.DeviceId:N}/heartbeat")
        {
            Content = JsonContent.Create(new
            {
                timestamp,
                signature
            })
        };

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Heartbeat rejected ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }
    }
}
