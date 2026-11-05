using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Multilider.Operations;
using MSMultilider.Comercial.Services;

// Loaded by DOTNET_STARTUP_HOOKS only in the isolated acceptance processes.
public static class StartupHook
{
    public static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("MULTILIDER_TEST_ISOLATED") != "1" ||
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Testing" ||
            Environment.GetEnvironmentVariable("MULTILIDER_F5_BOUNDARY_DIR") is not { } directory)
            throw new InvalidOperationException("Isolated F5 boundary fixture required.");

        DiagnosticListener.AllListeners.Subscribe(new Subscription<DiagnosticListener>(listener =>
        {
            if (listener.Name != "Microsoft.Extensions.Hosting") return;
            listener.Subscribe(new Subscription<KeyValuePair<string, object?>>(entry =>
            {
                if (entry is not { Key: "HostBuilding", Value: IHostBuilder host }) return;
                host.ConfigureServices((_, services) =>
                {
                    services.AddSingleton(new Checkpoint(directory));
                    services.AddSingleton<IOperationObserver>(sp => sp.GetRequiredService<Checkpoint>());
                    services.AddSingleton<OperationChannel, ObservedChannel>();
                });
            }));
        }));
    }

    private sealed class Subscription<T>(Action<T> receive) : IObserver<T>
    {
        public void OnNext(T value) => receive(value);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }

    private sealed class Checkpoint(string directory) : IOperationObserver
    {
        public void Boundary(Guid operation, string step, bool after) =>
            Wait(operation, step, after ? "after" : "before");

        public void Wait(Guid operation, string step, string side)
        {
            var marker = Path.Combine(directory, $"{operation:N}-{step}-{side}");
            var armed = marker + ".arm";
            if (!File.Exists(armed)) return;
            try { File.Move(armed, marker + ".claimed"); }
            catch (FileNotFoundException) { return; } // The other worker claimed it.

            File.WriteAllText(marker + ".hit", Environment.ProcessId.ToString());
            if (!SpinWait.SpinUntil(() =>
            {
                Thread.Sleep(25);
                return File.Exists(marker + ".release");
            }, TimeSpan.FromSeconds(90)))
                throw new TimeoutException("F5 acceptance fixture boundary was not released.");
            if (File.ReadAllText(marker + ".release") == "lose-response")
                throw new HttpRequestException("Fixture discarded a committed response.");
        }
    }

    private sealed class ObservedChannel(IConfiguration config, Checkpoint boundary) : OperationChannel(config)
    {
        public override async Task<JsonElement> Send(string service, HttpMethod method, string path, object? body,
            CancellationToken ct, string? bearer = null)
        {
            var result = await base.Send(service, method, path, body, ct, bearer);
            const string prefix = "internal/identity/v1/operaciones/";
            const string suffix = "/provision";
            if (service == "seguridad" && method == HttpMethod.Post && path.StartsWith(prefix) && path.EndsWith(suffix)
                && Guid.TryParse(path[prefix.Length..^suffix.Length], out var operation))
                boundary.Wait(operation, "ProvisionarIdentidad", "after");
            return result;
        }
    }
}
