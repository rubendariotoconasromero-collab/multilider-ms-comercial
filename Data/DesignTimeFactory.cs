using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace MSMultilider.Comercial.Data;
public sealed class DesignTimeFactory:IDesignTimeDbContextFactory<AppDbContext> {
    public AppDbContext CreateDbContext(string[] args)=>new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=localhost;Database=__design_only_not_created__;Integrated Security=True;TrustServerCertificate=True").Options);
}
