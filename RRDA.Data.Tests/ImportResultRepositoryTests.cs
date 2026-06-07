using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RRDA.Core;
using Xunit;

namespace RRDA.Data.Tests;

public sealed class ImportResultRepositoryTests
{
    [Fact]
    public async Task CountExistingAsync_UsesFileNameAndReportTypeAcrossDifferentPaths()
    {
        await using var db = CreateDbContext();
        var reportType = CreateReportType();
        db.ReportFiles.Add(CreateReportFile(reportType, @"C:\client-a\report.xlsx"));
        await db.SaveChangesAsync();

        IImportResultRepository repository = new ImportResultRepository();

        var count = await repository.CountExistingAsync("report.xlsx", reportType.Key, db);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SaveAsync_BlockRejectsSameFileNameAndReportTypeFromDifferentPath()
    {
        await using var db = CreateDbContext();
        var reportType = CreateReportType();
        db.ReportFiles.Add(CreateReportFile(reportType, @"C:\client-a\report.xlsx"));
        await db.SaveChangesAsync();

        IImportResultRepository repository = new ImportResultRepository();
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
                db,
                duplicateStrategy: DuplicateImportStrategy.Block));

        Assert.Equal("report.xlsx", exception.FileName);
        Assert.Equal(1, exception.ExistingCount);
        Assert.Single(db.ReportFiles);
    }

    private static RRDADbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RRDADbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new RRDADbContext(options);
    }

    private static ReportType CreateReportType() =>
        new()
        {
            Key = "TEST",
            Name = "Test",
            Files = [],
            TabularSessions = []
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
}
