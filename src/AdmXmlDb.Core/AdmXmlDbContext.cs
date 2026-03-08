using Microsoft.EntityFrameworkCore;
using AdmXmlDb.Core.Entities;

namespace AdmXmlDb.Core;

public class AdmXmlDbContext : DbContext
{
    private readonly string _dbPath;

    public AdmXmlDbContext() : this(Constants.GetDefaultDbPath())
    {
    }

    public AdmXmlDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    public DbSet<SmtpSettings> SmtpSettings => Set<SmtpSettings>();
    public DbSet<IntegrationTask> Tasks => Set<IntegrationTask>();
    public DbSet<TaskMapping> TaskMappings => Set<TaskMapping>();
    public DbSet<TargetDirectoryComponent> TargetDirectoryComponents => Set<TargetDirectoryComponent>();
    public DbSet<ExecutionLog> ExecutionLogs => Set<ExecutionLog>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SmtpSettings>(e =>
        {
            e.HasIndex(x => x.Id).IsUnique();
        });

        modelBuilder.Entity<IntegrationTask>(e =>
        {
            e.HasMany(x => x.Mappings)
                .WithOne(x => x.Task)
                .HasForeignKey(x => x.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.TargetDirectoryComponents)
                .WithOne(x => x.Task)
                .HasForeignKey(x => x.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TaskMapping>(e =>
        {
            e.HasOne(x => x.Task)
                .WithMany(x => x.Mappings)
                .HasForeignKey(x => x.TaskId);
        });

        modelBuilder.Entity<TargetDirectoryComponent>(e =>
        {
            e.HasOne(x => x.Task)
                .WithMany(x => x.TargetDirectoryComponents)
                .HasForeignKey(x => x.TaskId);
        });
    }
}
