using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using MSMultilider.Comercial.Infrastructure;
namespace MSMultilider.Comercial.Services;
public sealed record AvailabilityReply(int Status,JsonElement Body);
public interface IAvailabilityClient {
    Task<AvailabilityReply> Send(string method,string path,string? payload,CancellationToken ct);
}
public sealed class AvailabilityClient(HttpClient client,IHttpContextAccessor accessor):IAvailabilityClient {
    public async Task<AvailabilityReply> Send(string method,string path,string? payload,CancellationToken ct) {
        if(!System.Text.RegularExpressions.Regex.IsMatch(path,"^internal/catalogo/v1/(propiedades/[1-9][0-9]*/reservas|reservas/[a-f0-9-]{36}/(renovar|liberar|confirmar)|operaciones/[a-f0-9-]{36}/reserva|comandos/[a-f0-9-]{36})$"))throw new InvalidOperationException("Unknown availability path.");
        var context=accessor.HttpContext??throw new BusinessException("Se necesita una sesión vigente.",401);
        if(context.User.Identity?.IsAuthenticated!=true)throw new BusinessException("Se necesita una sesión vigente.",401);
        using var request=new HttpRequestMessage(new HttpMethod(method),path);
        if(!AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization,out var auth)||auth.Scheme!="Bearer")throw new BusinessException("Credencial requerida.",401);
        request.Headers.Authorization=auth;
        request.Headers.Add("X-Correlation-ID",Guid.NewGuid().ToString());
        if(payload is not null)request.Content=new StringContent(payload,Encoding.UTF8,"application/json");
        using var response=await client.SendAsync(request,ct);
        var bytes=await response.Content.ReadAsByteArrayAsync(ct);
        if(bytes.Length>65536)throw new HttpRequestException("Invalid availability reply.");
        JsonElement body;
        try {body=JsonSerializer.Deserialize<JsonElement>(bytes);} catch(JsonException){throw new HttpRequestException("Invalid availability reply.");}
        return new((int)response.StatusCode,body);
    }
    public static HttpMessageHandler Handler(IConfiguration config) {
        var path=config["Availability:ClientCertificatePath"];
        var pins=(config["Availability:ServerCertificateSha256"]??"").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Replace(":","").ToUpperInvariant()).ToArray();
        if(string.IsNullOrWhiteSpace(path)||pins.Length==0||pins.Any(x=>x.Length!=64||!x.All(Uri.IsHexDigit)))throw new BusinessException("INTEGRATION_NOT_READY: configura TLS de disponibilidad.",503);
        var cert=X509CertificateLoader.LoadPkcs12FromFile(path,config["Availability:ClientCertificatePassword"],OperatingSystem.IsWindows()?X509KeyStorageFlags.DefaultKeySet:X509KeyStorageFlags.EphemeralKeySet);
        var handler=new SocketsHttpHandler {AllowAutoRedirect=false,ConnectTimeout=TimeSpan.FromSeconds(2)};
        handler.SslOptions.ClientCertificates=new X509CertificateCollection{cert};
        handler.SslOptions.RemoteCertificateValidationCallback=(_,remoteCertificate,_,errors)=>remoteCertificate is X509Certificate2 certificate && (errors&System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch)==0 && certificate.NotBefore.ToUniversalTime()<=DateTime.UtcNow && certificate.NotAfter.ToUniversalTime()>DateTime.UtcNow && pins.Contains(certificate.GetCertHashString(HashAlgorithmName.SHA256),StringComparer.Ordinal) && certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(x=>x.EnhancedKeyUsages.Cast<Oid>().Any(oid=>oid.Value=="1.3.6.1.5.5.7.3.1"));
        return handler;
    }
}
