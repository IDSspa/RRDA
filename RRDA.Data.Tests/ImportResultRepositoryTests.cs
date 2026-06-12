using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RRDA.Core;
using Xunit;

namespace RRDA.Data.Tests;

public sealed class ImportResultRepositoryTests
{
    [Fact]
    public void ReportProperty_IsNotSubjectKeyByDefault()
    {
        var property = new ReportProperty
        {
            ReportEntity = null!,
            Name = "value",
            DataType = "string"
        };

        Assert.False(property.IsSubjectKey);
    }

    [Fact]
    public async Task CountExistingAsync_UsesFileNameAndReportTypeAcrossDifferentPaths()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        var reportType = CreateReportType();
        db.ReportFiles.Add(CreateReportFile(reportType, @"C:\client-a\report.xlsx"));
        await db.SaveChangesAsync();

        IImportResultRepository repository = new ImportResultRepository(factory);

        var count = await repository.CountExistingAsync("report.xlsx", reportType.Key);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SaveAsync_BlockRejectsSameFileNameAndReportTypeFromDifferentPath()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        var reportType = CreateReportType();
        db.ReportFiles.Add(CreateReportFile(reportType, @"C:\client-a\report.xlsx"));
        await db.SaveChangesAsync();

        IImportResultRepository repository = new ImportResultRepository(factory);
        var file = new ImportFileItem(
            "report.xlsx",
            Length: 100,
            LastWriteTime: DateTime.UtcNow,
            Type: reportType.Key,
            FullPath: "RRDA.Web upload: report.xlsx");
        var result = new ImportResult
        {
            ReportTypeKey = reportType.Key,
            Success = true,
            Entities = []
        };

        var exception = await Assert.ThrowsAsync<DuplicateImportException>(() =>
            repository.SaveAsync(
                file,
                result,
                duplicateStrategy: DuplicateImportStrategy.Block));

        Assert.Equal("report.xlsx", exception.FileName);
        Assert.Equal(1, exception.ExistingCount);
        await using var verificationDb = factory.CreateDbContext();
        Assert.Single(verificationDb.ReportFiles);
    }

    [Fact]
    public async Task SaveAsync_PersistsImportedReferenceWithoutResolvedTargetFile()
    {
        var factory = CreateDbContextFactory();
        await using (var db = factory.CreateDbContext())
        {
            db.ReportTypes.Add(CreateReportType("RADAR"));
            db.ReportTypes.Add(CreateReportType("ALI"));
            await db.SaveChangesAsync();
        }

        IImportResultRepository repository = new ImportResultRepository(factory);
        var result = new ImportResult
        {
            ReportTypeKey = "RADAR",
            Success = true,
            Entities =
            [
                new ReportEntityDto
                {
                    EntityKind = "Composizione",
                    Key = "SN_ALI",
                    Properties = new Dictionary<string, string>
                    {
                        ["value"] = "12345",
                        ["data_type"] = "string"
                    },
                    Reference = new ReportReferenceDto
                    {
                        TargetReportTypeKey = "ALI",
                        TargetKeyField = "Serial",
                        TargetKeyValue = "12345"
                    }
                }
            ]
        };

        await repository.SaveAsync(
            new ImportFileItem(
                "radar.xlsx",
                100,
                DateTime.UtcNow,
                "RADAR",
                @"C:\reports\radar.xlsx"),
            result);

        await using var verificationDb = factory.CreateDbContext();
        var reference = await verificationDb.ReportReferences
            .Include(item => item.TargetReportType)
            .SingleAsync();
        Assert.Equal(ReportReferenceOrigin.Imported, reference.Origin);
        Assert.Equal("ALI", reference.TargetReportType!.Key);
        Assert.Equal("Serial", reference.TargetKeyField);
        Assert.Equal("12345", reference.TargetKeyValue);
        Assert.Null(reference.TargetReportFileId);
    }

    [Fact]
    public async Task SaveAsync_ReplaceRemovesManualReferencesToReplacedFile()
    {
        var factory = CreateDbContextFactory();
        await using (var db = factory.CreateDbContext())
        {
            var targetType = CreateReportType();
            var sourceType = CreateReportType("RADAR");
            var target = CreateReportFile(targetType, @"C:\old\report.xlsx");
            var source = new ReportFile
            {
                FileName = "radar.xlsx",
                FullPath = @"C:\radar.xlsx",
                UploadedAt = DateTime.UtcNow,
                ReportType = sourceType,
                Entities = [],
                FileLastModify = DateTime.UtcNow
            };
            db.ReportReferences.Add(new ReportReference
            {
                SourceReportFile = source,
                TargetReportFile = target,
                Origin = ReportReferenceOrigin.Manual,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        IImportResultRepository repository = new ImportResultRepository(factory);
        await repository.SaveAsync(
            new ImportFileItem(
                "report.xlsx",
                100,
                DateTime.UtcNow,
                "TEST",
                @"C:\new\report.xlsx"),
            new ImportResult
            {
                ReportTypeKey = "TEST",
                Success = true,
                Entities = []
            },
            duplicateStrategy: DuplicateImportStrategy.Replace);

        await using var verificationDb = factory.CreateDbContext();
        Assert.Empty(verificationDb.ReportReferences);
        Assert.Single(verificationDb.ReportFiles.Where(file => file.ReportType.Key == "TEST"));
    }

    private static IDbContextFactory<RRDADbContext> CreateDbContextFactory()
    {
        var options = new DbContextOptionsBuilder<RRDADbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new TestDbContextFactory(options);
    }

    private static ReportType CreateReportType(string key = "TEST") =>
        new()
        {
            Key = key,
            Name = key,
            Files = []
        };

    private static ReportFile CreateReportFile(ReportType reportType, string fullPath) =>
        new()
        {
            FileName = "report.xlsx",
            FullPath = fullPath,
            UploadedAt = DateTime.UtcNow,
            ReportType = reportType,
            ReportTypeId = reportType.Id,
            Entities = [],
            FileLastModify = DateTime.UtcNow
        };

    private sealed class TestDbContextFactory(DbContextOptions<RRDADbContext> options)
        : IDbContextFactory<RRDADbContext>
    {
        public RRDADbContext CreateDbContext() => new(options);
    }
}
