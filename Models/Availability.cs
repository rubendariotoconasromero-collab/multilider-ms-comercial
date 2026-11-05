using System.Text.Json.Serialization;
namespace MSMultilider.Comercial.Models;
public sealed class AvailabilityIntent {
    public Guid Id {get;set;}
    [JsonIgnore] public string ActorIssuer {get;set;}="";
    [JsonIgnore] public string ActorSubject {get;set;}="";
    public int PropiedadId {get;set;}
    public int ClienteId {get;set;}
    public int AsesorId {get;set;}
    public DateTime CreatedAt {get;set;}=DateTime.UtcNow;
    public DateTime UpdatedAt {get;set;}=DateTime.UtcNow;
}
public sealed class AvailabilityCommand {
    public Guid Id {get;set;}
    public Guid IntentId {get;set;}
    public AvailabilityIntent Intent {get;set;}=null!;
    public string Action {get;set;}="";
    public string RequestHash {get;set;}="";
    [JsonIgnore] public string Payload {get;set;}="";
    public string State {get;set;}="Pendiente";
    public int? HttpStatus {get;set;}
    public string? Result {get;set;}
    public DateTime CreatedAt {get;set;}=DateTime.UtcNow;
    public DateTime UpdatedAt {get;set;}=DateTime.UtcNow;
}
