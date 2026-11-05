using System.Security.Cryptography;
using StbImageSharp;
namespace MSMultilider.Comercial.Infrastructure;
public sealed class ProfilePhotos(IWebHostEnvironment env,IConfiguration config) {
    private string Root=>Path.GetFullPath(config["Storage:PhotosPath"]??Path.Combine(env.ContentRootPath,"storage","profile-photos"));
    public async Task<(string Id,string Mime)> Save(IFormFile file,CancellationToken ct) {
        Api.Require(file.Length is >0 and <=2*1024*1024,"La fotografía admite hasta 2 MB.");
        await using var stream=file.OpenReadStream();using var buffer=new MemoryStream();await stream.CopyToAsync(buffer,ct);
        var bytes=buffer.ToArray();string mime;
        if(bytes.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10}))mime="image/png";
        else if(bytes.AsSpan().StartsWith(new byte[]{255,216,255}))mime="image/jpeg";
        else if(bytes.AsSpan().StartsWith("GIF8"u8))mime="image/gif";
        else throw new BusinessException("Formato de imagen no permitido.");
        try {
            using var image=new MemoryStream(bytes);
            var info=ImageInfo.FromStream(image);
            Api.Require(info.HasValue&&info.Value.Width is >0 and <=8000&&info.Value.Height is >0 and <=8000&&(long)info.Value.Width*info.Value.Height<=32000000,"Dimensiones de imagen inválidas.");
            image.Position=0;_=ImageResult.FromStream(image,ColorComponents.RedGreenBlueAlpha);
        }catch(BusinessException){throw;}catch(Exception){throw new BusinessException("Imagen no válida.");}
        var id=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Directory.CreateDirectory(Root);var path=Path.Combine(Root,id);
        if(!File.Exists(path)) {
            var temporary=Path.Combine(Root,Guid.NewGuid().ToString("N")+".tmp");
            try {
                await File.WriteAllBytesAsync(temporary,bytes,ct);
                try {File.Move(temporary,path,false);} catch(IOException) when(File.Exists(path)) { }
            } finally {if(File.Exists(temporary))File.Delete(temporary);}
        }
        return(id,mime);
    }
    public async Task<byte[]> Read(string? id,CancellationToken ct) {
        Api.Require(id?.Length==64&&id.All(Uri.IsHexDigit),"Fotografía no encontrada.",404);
        var path=Path.Combine(Root,id!);Api.Require(File.Exists(path),"Fotografía no encontrada.",404);
        return await File.ReadAllBytesAsync(path,ct);
    }
}
