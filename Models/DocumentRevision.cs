namespace MSMultilider.Comercial.Models;
public sealed class DocumentRevision {
    public Guid Id{get;set;}=Guid.NewGuid();
    public string Kind{get;set;}="";
    public int ResourceId{get;set;}
    public string StorageId{get;set;}="";
    public string Sha256{get;set;}="";
    public DateTime CreatedAt{get;set;}=DateTime.UtcNow;
}
