using Microsoft.EntityFrameworkCore;
using MSMultilider.Comercial.Models;
using MSMultilider.Comercial.Infrastructure;
namespace MSMultilider.Comercial.Data;
public sealed class AppDbContext(DbContextOptions<AppDbContext> options):ServiceDbContext(options) {
    public DbSet<Cliente> Clientes=>Set<Cliente>();
    public DbSet<Asesor> Asesores=>Set<Asesor>();
    public DbSet<Venta> Ventas=>Set<Venta>();
    public DbSet<Contrato> Contratos=>Set<Contrato>();
    public DbSet<Entrega> Entregas=>Set<Entrega>();
    public DbSet<AvailabilityIntent> AvailabilityIntents=>Set<AvailabilityIntent>();
    public DbSet<AvailabilityCommand> AvailabilityCommands=>Set<AvailabilityCommand>();
    protected override void OnModelCreating(ModelBuilder m) {
        m.Entity<SaleOperation>().HasKey(x=>x.Id);
        m.Entity<CommercialCommand>().HasKey(x=>x.Id);
        m.Entity<CommercialCommand>().HasIndex(x=>x.OperationId);
        m.Entity<ProfileOperation>().HasKey(x=>x.Id);
        m.Entity<DocumentRevision>().HasKey(x=>x.Id);
        m.Entity<SaleOperation>().Property(x=>x.Stamp).IsConcurrencyToken();
        m.Entity<SaleOperation>().HasIndex(x=>new{x.State,x.NextAttempt});
        m.Entity<OperationStep>().HasKey(x=>x.Id);
        m.Entity<OperationStep>().HasIndex(x=>new{x.OperationId,x.Step}).IsUnique();
        m.Entity<Cliente>().HasIndex(x=>x.Ci).IsUnique();
        m.Entity<Venta>().HasIndex(x=>x.SolicitudId).IsUnique();
        m.Entity<Venta>().HasIndex(x=>x.PropiedadActivaId).IsUnique().HasFilter("[PropiedadActivaId] IS NOT NULL");
        m.Entity<Venta>().HasOne(x=>x.Cliente).WithMany().HasForeignKey(x=>x.ClienteId);
        m.Entity<Venta>().HasOne(x=>x.Asesor).WithMany().HasForeignKey(x=>x.AsesorId);
        m.Entity<Contrato>().HasIndex(x=>x.NotaVentaId).IsUnique();
        m.Entity<Contrato>().HasIndex(x=>x.CodigoContrato).IsUnique();
        m.Entity<Contrato>().HasOne(x=>x.NotaVenta).WithMany().HasForeignKey(x=>x.NotaVentaId);
        m.Entity<Entrega>().HasIndex(x=>x.ContratoId).IsUnique();
        m.Entity<Entrega>().HasOne(x=>x.Contrato).WithMany().HasForeignKey(x=>x.ContratoId);
        m.Entity<AvailabilityIntent>().HasKey(x=>x.Id);
        m.Entity<AvailabilityCommand>().HasKey(x=>x.Id);
        m.Entity<AvailabilityCommand>().HasOne(x=>x.Intent).WithMany().HasForeignKey(x=>x.IntentId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AvailabilityCommand>().HasIndex(x=>new{x.IntentId,x.CreatedAt});
        base.OnModelCreating(m);
        m.Entity<CommercialCommand>().Property(x=>x.Result).HasColumnType(Database.IsSqlServer()?"nvarchar(max)":"TEXT").Metadata.SetMaxLength(null);
        m.Entity<CommercialCommand>().Property(x=>x.Hash).HasMaxLength(64);
        m.Entity<SaleOperation>().Property(x=>x.State).HasMaxLength(32);
        m.Entity<SaleOperation>().Property(x=>x.Step).HasMaxLength(64);
        m.Entity<SaleOperation>().Property(x=>x.Kind).HasMaxLength(32);
        m.Entity<OperationStep>().Property(x=>x.Step).HasMaxLength(64);
        m.Entity<Venta>().Property(x=>x.DocumentSnapshot).HasColumnType(Database.IsSqlServer()?"nvarchar(max)":"TEXT").Metadata.SetMaxLength(null);
        m.Entity<ProfileOperation>().Property(x=>x.Payload).HasColumnType(Database.IsSqlServer()?"nvarchar(max)":"TEXT").Metadata.SetMaxLength(null);
        m.Entity<ProfileOperation>().Property(x=>x.PayloadHash).HasMaxLength(64);
        foreach(var name in new[]{nameof(SaleOperation.Payload),nameof(SaleOperation.Offer),nameof(SaleOperation.Reservation)})
            m.Entity<SaleOperation>().Property(name).HasColumnType(Database.IsSqlServer()?"nvarchar(max)":"TEXT").Metadata.SetMaxLength(null);
        m.Entity<SaleOperation>().Property(x=>x.PayloadHash).HasMaxLength(64);
        m.Entity<OperationStep>().Property(x=>x.Result).HasColumnType(Database.IsSqlServer()?"nvarchar(max)":"TEXT").Metadata.SetMaxLength(null);
        m.Entity<AvailabilityIntent>().Property(x=>x.ActorIssuer).HasMaxLength(255);
        m.Entity<AvailabilityIntent>().Property(x=>x.ActorSubject).HasMaxLength(191);
        m.Entity<AvailabilityCommand>().Property(x=>x.RequestHash).HasMaxLength(64);
        m.Entity<AvailabilityCommand>().Property(x=>x.Payload).HasColumnType(Database.IsSqlServer()?"nvarchar(max)":"TEXT");
        m.Entity<AvailabilityCommand>().Property(x=>x.Result).HasColumnType(Database.IsSqlServer()?"nvarchar(max)":"TEXT");
        m.Entity<AvailabilityCommand>().Property(x=>x.Payload).Metadata.SetMaxLength(null);
        m.Entity<AvailabilityCommand>().Property(x=>x.Result).Metadata.SetMaxLength(null);
        m.Entity<Venta>().Property(x=>x.SolicitudHash).HasMaxLength(64);
        m.Entity<Cliente>().Property(x=>x.Ci).HasMaxLength(50);
        m.Entity<Contrato>().Property(x=>x.CodigoContrato).HasMaxLength(100);
    }
}

