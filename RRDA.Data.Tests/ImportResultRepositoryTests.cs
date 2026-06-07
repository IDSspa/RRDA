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

    private static IDbContextFactory<RRDADbContext> CreateDbContextFactory()
    {
        var options = new DbContextOptionsBuilder<RRDADbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new TestDbContextFactory(options);
    }

    private static ReportType CreateReportType() =>
        new()
        {
            Key = "TEST",
            Name = "Test",
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
