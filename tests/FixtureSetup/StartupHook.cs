using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

        var checkpoint = new Checkpoint(directory);
        DiagnosticListener.AllListeners.Subscribe(new Subscription<DiagnosticListener>(listener =>
        {
            if (listener.Name == "Microsoft.EntityFrameworkCore")
            {
                listener.Subscribe(new Subscription<KeyValuePair<string, object?>>(entry =>
                {
                    if (entry.Key == RelationalEventId.TransactionCommitting.Name) checkpoint.Transaction(false);
                    if (entry.Key == RelationalEventId.TransactionCommitted.Name) checkpoint.Transaction(true);
                    if (entry.Key == RelationalEventId.TransactionDisposed.Name) checkpoint.ReceiptFinished();
                }));
                return;
            }
            if (listener.Name != "Microsoft.Extensions.Hosting") return;
            listener.Subscribe(new Subscription<KeyValuePair<string, object?>>(entry =>
            {
                if (entry is not { Key: "HostBuilding", Value: IHostBuilder host }) return;
                host.ConfigureServices((_, services) =>
                {
                    services.AddSingleton(checkpoint);
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
        private sealed record Stage(Guid Operation, string Step, bool Receipt);
        private readonly AsyncLocal<Stage?> stage = new();

        public void Boundary(Guid operation, string step, bool after)
        {
            stage.Value = new Stage(operation, step, after);
            Wait(operation, step, after ? "after" : "before");
        }

        public void Transaction(bool committed)
        {
            if (!committed && new HttpContextAccessor().HttpContext is {} request &&
                HttpMethods.IsPost(request.Request.Method) &&
                request.Request.Path.Value is { } route && route.StartsWith("/api/contratos/") && route.EndsWith("/gestionar") &&
                Guid.TryParse(request.Request.Headers["Idempotency-Key"], out var command))
                Wait(command, "Adjunto", "before");
            if (stage.Value is not { } current) return;
            var point = current.Receipt ? "Guardar-" + current.Step : current.Step == "PrepararVenta" ? "PersistirVenta" : null;
            if (point is not null) Wait(current.Operation, point, committed ? "after" : "before");
        }

        public void ReceiptFinished()
        {
            if (stage.Value is not { Receipt: true } current) return;
            var marker = Path.Combine(directory, $"{current.Operation:N}-{current.Step}-after");
            var watch = marker + ".watch";
            if (File.Exists(watch) && File.ReadAllText(watch) == Environment.ProcessId.ToString())
                File.WriteAllText(marker + ".finished", "");
        }

        public void BeforeRequest(HttpMethod method, string path, object? body)
        {
            if (stage.Value is { Receipt: false } action && IsFinalAction(action.Step, method, path, body))
            {
                var marker = Path.Combine(directory, $"{action.Operation:N}-Respuesta-{action.Step}-after");
                if (File.Exists(marker + ".claimed"))
                {
                    File.WriteAllText(marker + ".resent", "");
                    throw new InvalidOperationException("A committed command was sent again instead of querying its receipt.");
                }
            }
            if (stage.Value is not { Step: "PrepararFinanzas", Receipt: false } current ||
                method != HttpMethod.Post || !path.EndsWith("/preparar", StringComparison.Ordinal))
                return;
            var pathToFault = Path.Combine(directory, $"{current.Operation:N}-PrepararFinanzas.reject");
            if (File.Exists(pathToFault))
            {
                File.Move(pathToFault, pathToFault + ".claimed");
                throw new ProtocolException("Isolated fixture forced a preparation rejection.", 409);
            }
        }

        public void CommittedResponse(HttpMethod method, string path, object? body)
        {
            if (stage.Value is { Receipt: false } current && IsFinalAction(current.Step, method, path, body))
                Wait(current.Operation, "Respuesta-" + current.Step, "after");
        }

        private static bool IsFinalAction(string step, HttpMethod method, string path, object? body)
        {
            if (method != HttpMethod.Post) return false;
            // Status and renewal calls within a step are separate from its final
            // acknowledgement. Only block the successful response being tested.
            return step switch
            {
                "Reservar" => path.EndsWith("/reservar", StringComparison.Ordinal),
                "PrepararFinanzas" => path.EndsWith("/preparar", StringComparison.Ordinal),
                "AutorizarConfirmacion" => body is OperationCheck { Action: "pivot" },
                "ConfirmarPropiedad" => path.EndsWith("/confirmar", StringComparison.Ordinal),
                "ActivarFinanzas" => path.EndsWith("/activar", StringComparison.Ordinal),
                "Abortar" => body is OperationCheck { Action: "abort" },
                "AbortarFinanzas" => path.EndsWith("/abortar-preparacion", StringComparison.Ordinal),
                "LiberarReserva" => path.EndsWith("/liberar", StringComparison.Ordinal),
                "AutorizarAnulacion" => body is OperationCheck { Action: "cancel-pivot" },
                "CerrarFinanzas" => path.EndsWith("/cerrar", StringComparison.Ordinal),
                "RevertirPropiedad" => path.EndsWith("/revertir-confirmacion", StringComparison.Ordinal),
                _ => false,
            };
        }

        public void Wait(Guid operation, string step, string side)
        {
            var marker = Path.Combine(directory, $"{operation:N}-{step}-{side}");
            var armed = marker + ".arm";
            if (!File.Exists(armed)) return;
            try { File.Move(armed, marker + ".claimed"); }
            catch (FileNotFoundException) { return; } // The other worker claimed it.

            File.WriteAllText(marker + ".pid", Environment.ProcessId.ToString());
            File.Move(marker + ".pid", marker + ".hit");
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

    private sealed class ObservedChannel(IConfiguration config, Checkpoint boundary, ILogger<OperationChannel> logger) : OperationChannel(config, logger)
    {
        public override async Task<JsonElement> Send(string service, HttpMethod method, string path, object? body,
            CancellationToken ct, string? bearer = null)
        {
            boundary.BeforeRequest(method, path, body);
            var result = await base.Send(service, method, path, body, ct, bearer);
            boundary.CommittedResponse(method, path, body);
            const string prefix = "internal/identity/v1/operaciones/";
            const string suffix = "/provision";
            if (service == "seguridad" && method == HttpMethod.Post && path.StartsWith(prefix) && path.EndsWith(suffix)
                && Guid.TryParse(path[prefix.Length..^suffix.Length], out var operation))
                boundary.Wait(operation, "ProvisionarIdentidad", "after");
            return result;
        }
    }
}
