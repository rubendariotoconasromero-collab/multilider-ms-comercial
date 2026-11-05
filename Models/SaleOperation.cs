using System.Text.Json.Serialization;
namespace MSMultilider.Comercial.Models;
public sealed class SaleOperation {
    public Guid Id{get;set;}
    public Guid CommandId{get;set;}
    public string Kind{get;set;}="venta";
    public Guid? ParentId{get;set;}
    public int PropiedadId{get;set;}
    public int? VentaId{get;set;}
    public string State{get;set;}="Pendiente";
    public string Step{get;set;}="Admitiendo";
    [JsonIgnore]public string ActorIssuer{get;set;}="";
    [JsonIgnore]public string ActorSubject{get;set;}="";
    [JsonIgnore]public string Payload{get;set;}="";
    [JsonIgnore]public string PayloadHash{get;set;}="";
    [JsonIgnore]public string? Offer{get;set;}
    [JsonIgnore]public string? Reservation{get;set;}
    public string? ErrorCode{get;set;}
    public int Attempts{get;set;}
    public DateTime CreatedAt{get;set;}=DateTime.UtcNow;
    public DateTime UpdatedAt{get;set;}=DateTime.UtcNow;
    public DateTime NextAttempt{get;set;}=DateTime.UtcNow;
    [JsonIgnore]public Guid? LeaseOwner{get;set;}
    [JsonIgnore]public DateTime? LeaseUntil{get;set;}
    [JsonIgnore]public Guid Stamp{get;set;}=Guid.NewGuid();
}
public sealed class OperationStep {
    public Guid Id{get;set;}
    public Guid OperationId{get;set;}
    public string Step{get;set;}="";
    [JsonIgnore]public string Result{get;set;}="";
    public DateTime CompletedAt{get;set;}=DateTime.UtcNow;
}
