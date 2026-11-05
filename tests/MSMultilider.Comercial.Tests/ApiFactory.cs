using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.Services;

namespace MSMultilider.Comercial.Tests;
public sealed class ApiFactory:WebApplicationFactory<Program> {
    public bool UseLegacyCharacterizationGate {get;init;}=true;
    public IAvailabilityClient? Availability {get;init;}
    private sealed class FixtureGate:ISaleActivationGate {public void RequireReady() {}}
    public bool Development { get; init; }
    private readonly SqliteConnection connection=new("Data Source=:memory:");
    public const string TestKey="only-for-automated-tests-not-a-real-secret-0123456789";
    private readonly string storage=Path.Combine(Path.GetTempPath(),"multilider-test-"+Guid.NewGuid().ToString("N"));
    public HttpStatusCode RemoteStatus {get;set;}=HttpStatusCode.OK;
    public int RemoteCalls {get;private set;}
    public HttpStatusCode IdentityStatus {get;set;}=HttpStatusCode.OK;
    public int IdentityCalls {get;private set;}
    public int IdentitySubject {get;set;}=1;
    public string[]? AuthoritativePermissions {get;set;}
    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseEnvironment(Development ? "Development" : "Testing");
        builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?> {
            ["Jwt:Key"]=TestKey,["Jwt:Issuer"]="tests",["Jwt:Audience"]="tests",["Storage:Path"]=storage,
            ["Logging:LogLevel:Default"]="Error",["Logging:LogLevel:Microsoft"]="Error",["Logging:LogLevel:Microsoft.AspNetCore"]="Error"
        }));
        builder.ConfigureServices(services=>{
            connection.Open();
            if(UseLegacyCharacterizationGate){services.RemoveAll<ISaleActivationGate>();services.AddSingleton<ISaleActivationGate,FixtureGate>();}
            if(Availability is not null){services.RemoveAll<IAvailabilityClient>();services.AddSingleton(Availability);}
            services.AddHttpClient("identity-status").ConfigurePrimaryHttpMessageHandler(()=>new IdentityHandler(this));
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o=>o.UseSqlite(connection));
            services.AddHttpClient<PropiedadesClient>().ConfigurePrimaryHttpMessageHandler(()=>new Handler(this));
            services.AddHttpClient<FinanzasClient>().ConfigurePrimaryHttpMessageHandler(()=>new Handler(this));
        });
    }
    public HttpClient Client(params string[] permissions) {
        var client=CreateClient();
        using var scope=Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        if(permissions.Length>0) client.DefaultRequestHeaders.Authorization=new("Bearer",Token(permissions));
        return client;
    }
    public static readonly string[] AllPermissions=["acceso_clientes","acceso_asesores","acceso_ventas","acceso_historial_ventas",
        "acceso_contratos","acceso_entregas","acceso_pagos","acceso_ingresos","acceso_egresos"];
    public static string Token(string[] permissions,string issuer="tests",bool expired=false,int subject=1) {
        var claims=permissions.Select(p=>new Claim("permission",p)).Concat(new[] {new Claim("sub",subject.ToString()),new Claim("jti",Guid.NewGuid().ToString()),
            new Claim("ver","0",ClaimValueTypes.Integer64),new Claim("iat",DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds().ToString(),ClaimValueTypes.Integer64)});
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer,"tests",claims,
            DateTime.UtcNow.AddMinutes(-5),DateTime.UtcNow.AddMinutes(expired?-1:10),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestKey)),SecurityAlgorithms.HmacSha256)));
    }
    private sealed class IdentityHandler(ApiFactory factory):HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
            factory.IdentityCalls++;
            return Task.FromResult(new HttpResponseMessage(factory.IdentityStatus){Content=JsonContent.Create(new {id=factory.IdentitySubject,estado=true,
                rolesPermisos=(factory.AuthoritativePermissions ?? new JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization!.Parameter).Claims.Where(x=>x.Type=="permission").Select(x=>x.Value).ToArray()).Select(x=>new {nombrePermiso=x})})});
        }
    }
    private sealed class Handler(ApiFactory factory):HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
            factory.RemoteCalls++;
            if(factory.RemoteStatus!=HttpStatusCode.OK)return Task.FromResult(new HttpResponseMessage(factory.RemoteStatus));
            object body;
            var id=int.TryParse(request.RequestUri!.Segments.Last(),out var parsed)?parsed:1;
            body=new {id,codigo="PROP-"+id,precio_venta=1000m,moneda="BOB",estado="Disponible",activo=true};
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(body)});
        }
    }
    protected override void Dispose(bool disposing) {
        base.Dispose(disposing);
        if(disposing) {
            connection.Dispose();
            var root=Path.GetFullPath(storage);
            if(!root.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(root).StartsWith("multilider-test-"))throw new InvalidOperationException("Unexpected test storage path.");
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
public static class HttpTest {
    public static async Task<JsonElement> Json(this HttpResponseMessage response) {
        var body=await response.Content.ReadAsStringAsync();
        Xunit.Assert.True(response.IsSuccessStatusCode,$"HTTP {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }
}

