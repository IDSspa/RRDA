using Microsoft.EntityFrameworkCore;

namespace RRDA.Data
{
    public class RRDADbContext : DbContext
    {
        public RRDADbContext(DbContextOptions<RRDADbContext> options) : base(options) { }

        public DbSet<ReportFile> ReportFiles { get; set; } = null!;
        public DbSet<ReportEntity> ReportEntities { get; set; } = null!;
        public DbSet<ReportProperty> ReportProperties { get; set; } = null!;
        public DbSet<ReportType> ReportTypes { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
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
            });

            modelBuilder.Entity<ReportEntity>(b =>
            {
                b.ToTable("ReportEntities");

                b.HasKey(x => x.Id);

                b.Property(x => x.EntityKind)
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
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}
