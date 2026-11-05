namespace MSMultilider.Comercial.Infrastructure;
public sealed class DocumentStore(IWebHostEnvironment env,IConfiguration config) {
    private string DirectoryPath=>Path.GetFullPath(config["Storage:Path"] ?? Path.Combine(env.ContentRootPath,"storage","documentos"));
    public async Task<string> Save(IFormFile file,CancellationToken ct) {
        Api.Require(file.Length is >=5 and <=10*1024*1024,"El documento debe ser PDF de hasta 10 MB.");
        await using var input=file.OpenReadStream();
        var header=new byte[5];await input.ReadExactlyAsync(header,ct);
        Api.Require(header.SequenceEqual("%PDF-"u8.ToArray()),"El archivo no contiene un PDF válido.");
        Directory.CreateDirectory(DirectoryPath);
        var id=Guid.NewGuid().ToString("N");
        var temporary=Path.Combine(DirectoryPath,id+".tmp");
        try {
            await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                await output.WriteAsync(header,ct);await input.CopyToAsync(output,ct);await output.FlushAsync(ct);
            }
            File.Move(temporary,Path.Combine(DirectoryPath,id+".pdf"));
        } finally {if(File.Exists(temporary))File.Delete(temporary);}
        return id;
    }
    public async Task<byte[]> Read(string? id,CancellationToken ct) {
        Api.Require(Guid.TryParseExact(id,"N",out var guid),"No hay documento adjunto.",404);
        var path=Path.Combine(DirectoryPath,guid.ToString("N")+".pdf");
        Api.Require(File.Exists(path),"Documento no encontrado.",404);
        return await File.ReadAllBytesAsync(path,ct);
    }
    public (int Candidates,int Deleted) Cleanup(ISet<string> retained,bool apply) {
        if(!Directory.Exists(DirectoryPath))return(0,0);
        var count=0;var deleted=0;var cutoff=DateTime.UtcNow.AddDays(-2);
        foreach(var path in Directory.EnumerateFiles(DirectoryPath,"*",SearchOption.TopDirectoryOnly)) {
            var ext=Path.GetExtension(path);var id=Path.GetFileNameWithoutExtension(path);
            if(ext is not (".pdf" or ".tmp")||!Guid.TryParseExact(id,"N",out _)||retained.Contains(id)||File.GetLastWriteTimeUtc(path)>=cutoff)continue;
            count++;if(apply){File.Delete(path);deleted++;}
        }
        return(count,deleted);
    }
}

