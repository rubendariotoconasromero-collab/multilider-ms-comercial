using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using MSMultilider.Comercial.Data;
using MSMultilider.Comercial.Infrastructure;
using MSMultilider.Comercial.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower);
builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer")));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o=>Multilider.Identity.SessionValidation.Configure(o,builder.Configuration));
builder.Services.AddAuthorization(o => {
    foreach (var permission in new[] { "acceso_clientes","acceso_asesores","acceso_ventas","acceso_historial_ventas",
        "acceso_contratos","acceso_entregas","acceso_pagos","acceso_ingresos","acceso_egresos" })
        o.AddPolicy(permission,p=>p.RequireAuthenticatedUser().RequireClaim("permission",permission));
});
builder.Services.AddCors(o=>o.AddDefaultPolicy(p=>p.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? []).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => {
    o.SwaggerDoc("v1",new OpenApiInfo { Title="MSMultilider.Comercial",Version="v1" });
    o.AddSecurityDefinition("Bearer",new OpenApiSecurityScheme { Type=SecuritySchemeType.Http, Scheme="bearer",BearerFormat="JWT" });
    o.AddSecurityRequirement(document=>new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer",document)] = [] });
});
builder.Services.AddScoped<ComercialService>();
builder.Services.AddSingleton<ISaleActivationGate,SaleActivationGate>();
builder.Services.AddScoped<AvailabilityService>();
builder.Services.AddHttpClient<IAvailabilityClient,AvailabilityClient>((sp,c)=> {
    var config=sp.GetRequiredService<IConfiguration>();
    var url=config["Availability:BaseUrl"];
    if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment))throw new BusinessException("INTEGRATION_NOT_READY: configura Availability:BaseUrl con HTTPS.",503);
    c.BaseAddress=new Uri(url.TrimEnd('/')+"/");c.Timeout=TimeSpan.FromSeconds(5);
}).ConfigurePrimaryHttpMessageHandler(sp=>AvailabilityClient.Handler(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<DocumentStore>();
builder.Services.AddHttpClient<PropiedadesClient>((sp,c)=> {
    c.BaseAddress=new Uri(sp.GetRequiredService<IConfiguration>()["Services:Propiedades"]!);
    c.Timeout=TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient<FinanzasClient>((sp,c)=> {
    c.BaseAddress=new Uri(sp.GetRequiredService<IConfiguration>()["Services:Finanzas"]!);
    c.Timeout=TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient("identity-status",c=> {
    c.BaseAddress=new Uri((builder.Configuration["Services:Seguridad"] ?? "http://localhost:5211").TrimEnd('/')+"/");
    c.Timeout=TimeSpan.FromSeconds(5);
}).ConfigurePrimaryHttpMessageHandler(()=>new SocketsHttpHandler {AllowAutoRedirect=false,ConnectTimeout=TimeSpan.FromSeconds(2)});
var app=builder.Build();
if(args.Contains("--migrate-commercial")) {
    await using var scope=app.Services.CreateAsyncScope();
    await ComercialMigrations.Apply(scope.ServiceProvider.GetRequiredService<AppDbContext>(),builder.Configuration["Migrations:DatabaseName"]??"");
    return;
}
app.UseExceptionHandler();
if(app.Environment.IsDevelopment()) { app.UseSwagger();app.UseSwaggerUI(); }
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health",()=>Results.Ok(new {service="Comercial",status="ok"})).AllowAnonymous();
app.MapGet("/health/ready",async(AppDbContext db,CancellationToken ct)=> {
    if (!await db.Database.CanConnectAsync(ct)) return Results.StatusCode(503);
    return (await db.Database.GetPendingMigrationsAsync(ct)).Any()
        ? Results.StatusCode(503) : Results.Ok(new {status="ready"});
}).RequireAuthorization();
app.MapControllers();
app.Run();

public partial class Program { }

