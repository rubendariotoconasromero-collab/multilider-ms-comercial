using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MSMultilider.Comercial.Infrastructure;
using Xunit;
namespace MSMultilider.Comercial.Tests;

public sealed class ComercialTests {
    private static async Task<(int Cliente,int Asesor)> Catalogos(HttpClient client) {
        var customer=await (await client.PostAsJsonAsync("/api/clientes",new{ci="123",nombre_completo="Cliente Prueba"})).Json();
        var advisor=await (await client.PostAsJsonAsync("/api/asesores",new{nombre_completo="Asesor Prueba",porcentaje_comision=10})).Json();
        return(customer.GetProperty("id").GetInt32(),advisor.GetProperty("id").GetInt32());
    }
    private static object Venta(int cliente,int asesor,int propiedad=1,Guid? solicitud=null,decimal descuento=0) =>
        new{solicitud_id=solicitud??Guid.NewGuid(),cliente_id=cliente,asesor_id=asesor,propiedad_id=propiedad,
            fecha="2026-11-01",tipo_venta="CONTADO",descuento};
    [Fact] public async Task AutenticacionYPermisos() {
        using var app=new ApiFactory();using var client=app.Client();
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/clientes")).StatusCode);
        client.DefaultRequestHeaders.Authorization=new("Bearer",ApiFactory.Token(["otro_permiso"]));
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/clientes")).StatusCode);
        client.DefaultRequestHeaders.Authorization=new("Bearer",ApiFactory.Token(["acceso_clientes"],issuer:"otro"));
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/clientes")).StatusCode);
        client.DefaultRequestHeaders.Authorization=new("Bearer",ApiFactory.Token(["acceso_clientes"],expired:true));
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/clientes")).StatusCode);
    }
    [Fact] public async Task VentaCalculaComisionYContratoSinDuplicar() {
        using var app=new ApiFactory();using var client=app.Client(ApiFactory.AllPermissions);
        var(c,a)=await Catalogos(client);var request=Venta(c,a,solicitud:Guid.NewGuid(),descuento:100);
        var created=await(await client.PostAsJsonAsync("/api/ventas",request)).Json();
        var repeated=await(await client.PostAsJsonAsync("/api/ventas",request)).Json();
        Assert.Equal(created.GetProperty("id").GetInt32(),repeated.GetProperty("id").GetInt32());
        Assert.Equal(900,created.GetProperty("monto_liquido").GetDecimal());
        Assert.Equal(100,created.GetProperty("monto_comision").GetDecimal());
        var contracts=await(await client.GetAsync("/api/contratos")).Json();
        Assert.Equal(1,contracts.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/ventas",Venta(c,a))).StatusCode);
    }
    [Fact] public async Task DatosInvalidosYClientesInactivosNoGeneranVenta() {
        using var app=new ApiFactory();using var client=app.Client(ApiFactory.AllPermissions);
        var(c,a)=await Catalogos(client);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/clientes",new{ci="",nombre_completo=""})).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await client.PostAsJsonAsync("/api/ventas",Venta(c,a,descuento:1000))).StatusCode);
        await client.DeleteAsync($"/api/clientes/{c}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await client.PostAsJsonAsync("/api/ventas",Venta(c,a))).StatusCode);
        var list=await(await client.GetAsync("/api/ventas")).Json();Assert.Equal(0,list.GetProperty("total").GetInt32());
    }
    [Fact] public async Task CaidaDePropiedadesNoDejaVentaParcial() {
        using var app=new ApiFactory();using var client=app.Client(ApiFactory.AllPermissions);
        var(c,a)=await Catalogos(client);app.RemoteStatus=HttpStatusCode.BadGateway;
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.PostAsJsonAsync("/api/ventas",Venta(c,a))).StatusCode);
        var list=await(await client.GetAsync("/api/contratos")).Json();Assert.Equal(0,list.GetProperty("total").GetInt32());
    }
    [Fact] public async Task FirmaCreaEntregaYExigeResponsables() {
        using var app=new ApiFactory();using var client=app.Client(ApiFactory.AllPermissions);
        var(c,a)=await Catalogos(client);await(await client.PostAsJsonAsync("/api/ventas",Venta(c,a))).Json();
        var contracts=await(await client.GetAsync("/api/contratos")).Json();
        var contractId=contracts.GetProperty("data")[0].GetProperty("id").GetInt32();
        await(await client.PostAsJsonAsync($"/api/contratos/{contractId}/firmar",new{fecha_firma="2026-11-02",fecha_programada_entrega="2026-11-03"})).Json();
        var deliveries=await(await client.GetAsync("/api/entregas")).Json();
        var id=deliveries.GetProperty("data")[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await client.PutAsJsonAsync($"/api/entregas/{id}",new{
            estado="Entregado",fecha_programada="2026-11-03"})).StatusCode);
        await(await client.PutAsJsonAsync($"/api/entregas/{id}",new{estado="Entregado",fecha_programada="2026-11-03",
            fecha_entrega="2026-11-03",entregado_por="Asesor",recibido_por="Cliente"})).Json();
        Assert.Equal(HttpStatusCode.Conflict,(await client.PutAsJsonAsync("/api/ventas/1/anular",new{})).StatusCode);
    }
    [Fact] public async Task AnulacionSoloConfirmaSiFinanzasAcepta() {
        using var app=new ApiFactory();using var client=app.Client(ApiFactory.AllPermissions);
        var(c,a)=await Catalogos(client);await(await client.PostAsJsonAsync("/api/ventas",Venta(c,a))).Json();
        app.RemoteStatus=HttpStatusCode.Conflict;
        Assert.Equal(HttpStatusCode.Conflict,(await client.PutAsJsonAsync("/api/ventas/1/anular",new{})).StatusCode);
        var row=await(await client.GetAsync("/api/ventas/1")).Json();Assert.Equal("Activa",row.GetProperty("estado").GetString());
        app.RemoteStatus=HttpStatusCode.OK;
        await(await client.PutAsJsonAsync("/api/ventas/1/anular",new{})).Json();
        await(await client.PostAsJsonAsync("/api/ventas",Venta(c,a))).Json();
    }
    [Fact] public async Task DocumentosPrivadosSoloAceptanPdf() {
        using var app=new ApiFactory();using var client=app.Client(ApiFactory.AllPermissions);
        var(c,a)=await Catalogos(client);await(await client.PostAsJsonAsync("/api/ventas",Venta(c,a))).Json();
        using var bad=new MultipartFormDataContent();bad.Add(new ByteArrayContent("malicious"u8.ToArray()),"archivo","test.pdf");
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await client.PostAsync("/api/contratos/1/documento",bad)).StatusCode);
        var pdf=await client.GetByteArrayAsync("/api/contratos/1/generar-pdf");
        Assert.StartsWith("%PDF-",System.Text.Encoding.Latin1.GetString(pdf));
        using var good=new MultipartFormDataContent();good.Add(new ByteArrayContent(pdf),"archivo","../../outside.pdf");
        await(await client.PostAsync("/api/contratos/1/documento",good)).Json();
        Assert.Equal(pdf,await client.GetByteArrayAsync("/api/contratos/1/descargar"));
        var store=app.Services.GetRequiredService<DocumentStore>();
        foreach(var unsafeId in new[]{"../../outside.pdf","..\\outside.pdf","/outside.pdf","%2e%2e%2foutside.pdf"}) {
            var refused=await Assert.ThrowsAsync<BusinessException>(()=>store.Read(unsafeId,default));
            Assert.Equal(404,refused.Status);
        }
        client.DefaultRequestHeaders.Authorization=null;
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/contratos/1/descargar")).StatusCode);
    }
}

