using System.Text.Json.Serialization;
namespace MSMultilider.Comercial.Models;
public sealed class ProfileOperation {
    public Guid Id{get;set;}
    public string Kind{get;set;}="";
    public int ResourceId{get;set;}
    public string State{get;set;}="Pendiente";
    [JsonIgnore]public string ActorIssuer{get;set;}="";
    [JsonIgnore]public string ActorSubject{get;set;}="";
    [JsonIgnore]public string Payload{get;set;}="";
    [JsonIgnore]public string PayloadHash{get;set;}="";
    public string? ErrorCode{get;set;}
    public int Attempts{get;set;}
    public DateTime NextAttempt{get;set;}=DateTime.UtcNow;
    public DateTime UpdatedAt{get;set;}=DateTime.UtcNow;
}
