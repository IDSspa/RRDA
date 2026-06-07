using Microsoft.EntityFrameworkCore;

namespace RRDA.Data
{
    public class RRDADbContext(DbContextOptions<RRDADbContext> options) : DbContext(options)
    {
        public DbSet<ReportFile> ReportFiles { get; set; } = null!;
        public DbSet<ReportEntity> ReportEntities { get; set; } = null!;
        public DbSet<ReportProperty> ReportProperties { get; set; } = null!;
        public DbSet<ReportType> ReportTypes { get; set; } = null!;
        public DbSet<ReportBatch> ReportBatches { get; set; } = null!;
        public DbSet<AppUser> AppUsers { get; set; } = null!;
        public DbSet<AuditEvent> AuditEvents { get; set; } = null!;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AuditEvent>(b =>
            {
                b.ToTable("AuditEvents");
                b.HasKey(x => x.Id);
                b.Property(x => x.OccurredAtUtc).HasColumnType("datetime2").IsRequired();
                b.Property(x => x.Application).HasMaxLength(100).IsRequired();
                b.Property(x => x.MachineName).HasMaxLength(256).IsRequired();
                b.Property(x => x.UserName).HasMaxLength(256);
                b.Property(x => x.Operation).HasMaxLength(200).IsRequired();
                b.Property(x => x.Result).HasMaxLength(50).IsRequired();
                b.Property(x => x.EntityType).HasMaxLength(100);
                b.Property(x => x.EntityId).HasMaxLength(256);
                b.Property(x => x.Description).HasMaxLength(2000);
                b.Property(x => x.DetailsJson).HasColumnType("nvarchar(max)");
                b.Property(x => x.CorrelationId).HasMaxLength(100);
                b.Property(x => x.ClientIp).HasMaxLength(64);
                b.HasIndex(x => x.OccurredAtUtc);
                b.HasIndex(x => new { x.Operation, x.OccurredAtUtc });
                b.HasIndex(x => new { x.EntityType, x.EntityId });
            });

            modelBuilder.Entity<ReportFile>(b =>
            {
                b.ToTable("ReportFiles");

                b.HasKey(x => x.Id);

                b.Property(x => x.FileName)
                    .HasMaxLength(260)
                    .IsRequired();

                b.Property(x => x.UploadedAt)
                    .HasColumnType("datetime2")
                    .IsRequired();

                b.Property(x => x.ImportedBy)
                    .HasMaxLength(200);

                b.Property(x => x.FileLastModify)
                    .HasColumnType("datetime2")
                    .IsRequired();

                b.Property(x => x.ReportTypeId)
                    .IsRequired();

                // relazione verso ReportType (FK ReportTypeId)
                b.HasOne(x => x.ReportType)
                    .WithMany(t => t.Files)
                    .HasForeignKey(x => x.ReportTypeId)
                    .OnDelete(DeleteBehavior.Restrict);

                // relazione verso ReportEntity
                b.HasMany(x => x.Entities)
                    .WithOne(e => e.ReportFile)
                    .HasForeignKey(e => e.ReportFileId)
                    .OnDelete(DeleteBehavior.Cascade);

                // relazione verso ReportBatch
                b.HasOne(x => x.ReportBatch)
                    .WithMany(b => b.ReportFiles)
                    .HasForeignKey(x => x.ReportBatchId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<ReportEntity>(b =>
            {
                b.ToTable("ReportEntities");

                b.HasKey(x => x.Id);

                b.Property(x => x.ReportSheet)
                    .HasMaxLength(200)
                    .IsRequired();

                b.Property(x => x.Key)
                    .HasMaxLength(400)
                    .IsRequired();

                b.Property(x => x.ReportFileId)
                    .IsRequired();

                // indice per velocizzare lookup per file
                b.HasIndex(x => x.ReportFileId);

                // relazione verso ReportProperty
                b.HasMany(x => x.Properties)
                    .WithOne(p => p.ReportEntity)
                    .HasForeignKey(p => p.ReportEntityId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ReportProperty>(b =>
            {
                b.ToTable("ReportProperties");

                b.HasKey(x => x.Id);

                b.Property(x => x.Name)
                    .HasMaxLength(200)
                    .IsRequired();

                b.Property(x => x.Value)
                    .HasColumnType("nvarchar(max)");

                b.Property(x => x.Unit)
                    .HasMaxLength(50);

                b.Property(x => x.DataType)
                    .HasMaxLength(50)
                    .IsRequired();

                b.Property(x => x.IsSubjectKey)
                    .IsRequired()
                    .HasDefaultValue(false);

                b.Property(x => x.ReportEntityId)
                    .IsRequired();

                // FK verso ReportEntity con cancellazione a cascata
                b.HasOne(p => p.ReportEntity)
                    .WithMany(e => e.Properties)
                    .HasForeignKey(p => p.ReportEntityId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ReportType>(b =>
            {
                b.ToTable("ReportTypes");

                b.HasKey(x => x.Id);

                // chiave logica usata nelle lookup (colonna [Key] in DB)
                b.Property(x => x.Key)
                    .HasMaxLength(200)
                    .IsRequired();

                // rendiamo la Key unica
                b.HasIndex(x => x.Key)
                    .IsUnique();

                b.Property(x => x.Name)
                    .HasMaxLength(200)
                    .IsRequired();

                b.Property(x => x.Description)
                    .HasMaxLength(1000);

                b.Property(x => x.SubjectKind)
                    .HasConversion<int>()
                    .IsRequired();
            });

            modelBuilder.Entity<AppUser>(b =>
            {
                b.ToTable("AppUsers");
                b.HasKey(x => x.Id);

                // WindowsUsername univoco (case-insensitive a livello applicativo;
                // SQL Server collation gestisce il confronto lato DB)
                b.Property(x => x.WindowsUsername)
                    .HasMaxLength(256)
                    .IsRequired();

                b.HasIndex(x => x.WindowsUsername)
                    .IsUnique();

                b.Property(x => x.DisplayName)
                    .HasMaxLength(256);

                // Ruolo come intero: 0=Operator, 1=Supervisor, 2=Admin
                b.Property(x => x.Role)
                    .HasConversion<int>()
                    .IsRequired();

                b.Property(x => x.IsEnabled)
                    .IsRequired()
                    .HasDefaultValue(true);

                b.Property(x => x.CreatedAt)
                    .HasColumnType("datetime2")
                    .IsRequired();

                b.Property(x => x.LastLoginAt)
                    .HasColumnType("datetime2");

            });

            base.OnModelCreating(modelBuilder);
        }
    }
}
