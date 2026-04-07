using Microsoft.EntityFrameworkCore;
using OAM.Domain.Entities;

namespace OAM.Infrastructure.Data;

public class OamDbContext : DbContext
{
    public OamDbContext(DbContextOptions<OamDbContext> options) : base(options) { }

    public DbSet<DefinitionWorkflow> DefinitionsWorkflow => Set<DefinitionWorkflow>();
    public DbSet<VersionDefinitionWorkflow> VersionsDefinitionWorkflow => Set<VersionDefinitionWorkflow>();
    public DbSet<InstanceWorkflow> InstancesWorkflow => Set<InstanceWorkflow>();
    public DbSet<ExecutionTache> ExecutionsTache => Set<ExecutionTache>();
    public DbSet<CasTest> CasTests => Set<CasTest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DefinitionWorkflow>(entity =>
        {
            entity.ToTable("DefinitionsWorkflow");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Nom).IsUnique();
            entity.HasIndex(e => e.HashVersion);
            entity.Property(e => e.Nom).HasMaxLength(200).IsRequired();
            entity.Property(e => e.HashVersion).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Equipe).HasMaxLength(100);
            entity.Property(e => e.ContenuYaml).IsRequired();
        });

        modelBuilder.Entity<VersionDefinitionWorkflow>(entity =>
        {
            entity.ToTable("VersionsDefinitionWorkflow");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.HashVersion);
            entity.Property(e => e.HashVersion).HasMaxLength(64).IsRequired();
            entity.Property(e => e.DeployePar).HasMaxLength(200);
            entity.HasOne(e => e.DefinitionWorkflow)
                  .WithMany(d => d.Versions)
                  .HasForeignKey(e => e.DefinitionWorkflowId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InstanceWorkflow>(entity =>
        {
            entity.ToTable("InstancesWorkflow");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CorrelationId);
            entity.HasIndex(e => e.Etat);
            entity.HasIndex(e => e.DernierHeartbeat);
            entity.Property(e => e.CorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.HashVersionConfig).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Etat).HasConversion<string>().HasMaxLength(20);
            entity.HasOne(e => e.DefinitionWorkflow)
                  .WithMany(d => d.Instances)
                  .HasForeignKey(e => e.DefinitionWorkflowId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExecutionTache>(entity =>
        {
            entity.ToTable("ExecutionsTache");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.InstanceWorkflowId, e.NomTache });
            entity.Property(e => e.NomTache).HasMaxLength(200).IsRequired();
            entity.Property(e => e.TypeConnecteur).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Etat).HasConversion<string>().HasMaxLength(20);
            entity.HasOne(e => e.InstanceWorkflow)
                  .WithMany(i => i.Taches)
                  .HasForeignKey(e => e.InstanceWorkflowId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CasTest>(entity =>
        {
            entity.ToTable("CasTests");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.DefinitionWorkflowId, e.Nom }).IsUnique();
            entity.Property(e => e.Nom).HasMaxLength(200).IsRequired();
            entity.Property(e => e.DeployePar).HasMaxLength(200);
            entity.HasOne(e => e.DefinitionWorkflow)
                  .WithMany(d => d.CasTests)
                  .HasForeignKey(e => e.DefinitionWorkflowId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
