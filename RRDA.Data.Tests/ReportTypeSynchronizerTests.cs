using Microsoft.EntityFrameworkCore;
using RRDA.Core;
using RRDA.Core.Validator;
using Xunit;

namespace RRDA.Data.Tests;

public sealed class ReportTypeSynchronizerTests
{
    private readonly IReportTypeSynchronizer _synchronizer = new ReportTypeSynchronizer();

    [Fact]
    public async Task SyncAsync_InsertsNewReportType()
    {
        await using var db = CreateDbContext();
        var plugin = new TestPlugin("ALI", ReportSubjectKind.Component);

        var result = await _synchronizer.SyncAsync(db, [plugin]);

        var reportType = await db.ReportTypes.SingleAsync();
        Assert.Equal(["ALI"], result.Inserted);
        Assert.Empty(result.Updated);
        Assert.Equal("ALI", reportType.Key);
        Assert.Equal("ALI", reportType.Name);
        Assert.Equal(string.Empty, reportType.Description);
        Assert.Equal(ReportSubjectKind.Component, reportType.SubjectKind);
    }

    [Fact]
    public async Task SyncAsync_UpdatesOnlySubjectKindAndPreservesUserFields()
    {
        await using var db = CreateDbContext();
        db.ReportTypes.Add(CreateReportType(
            key: "ALI",
            name: "Nome personalizzato",
            description: "Descrizione personalizzata",
            subjectKind: ReportSubjectKind.Component));
        await db.SaveChangesAsync();

        var result = await _synchronizer.SyncAsync(
            db,
            [new TestPlugin("ALI", ReportSubjectKind.Radar)]);

        var reportType = await db.ReportTypes.SingleAsync();
        Assert.Empty(result.Inserted);
        Assert.Equal(["ALI"], result.Updated);
        Assert.Equal("Nome personalizzato", reportType.Name);
        Assert.Equal("Descrizione personalizzata", reportType.Description);
        Assert.Equal(ReportSubjectKind.Radar, reportType.SubjectKind);
    }

    [Fact]
    public async Task SyncAsync_MatchesExistingReportTypeIgnoringCase()
    {
        await using var db = CreateDbContext();
        db.ReportTypes.Add(CreateReportType("ali", "ALI", string.Empty, ReportSubjectKind.Component));
        await db.SaveChangesAsync();

        var result = await _synchronizer.SyncAsync(
            db,
            [new TestPlugin("ALI", ReportSubjectKind.SubAssembly)]);

        Assert.Empty(result.Inserted);
        Assert.Equal(["ALI"], result.Updated);
        Assert.Equal(1, await db.ReportTypes.CountAsync());
    }

    [Fact]
    public async Task SyncAsync_DoesNotDeleteReportTypesWithoutLoadedPlugin()
    {
        await using var db = CreateDbContext();
        db.ReportTypes.Add(CreateReportType("ALI", "ALI", string.Empty, ReportSubjectKind.Component));
        db.ReportTypes.Add(CreateReportType("LEGACY", "Legacy", string.Empty, ReportSubjectKind.Radar));
        await db.SaveChangesAsync();

        var result = await _synchronizer.SyncAsync(
            db,
            [new TestPlugin("ALI", ReportSubjectKind.Component)]);

        Assert.False(result.HasChanges);
        Assert.Equal(2, await db.ReportTypes.CountAsync());
        Assert.True(await db.ReportTypes.AnyAsync(t => t.Key == "LEGACY"));
    }

    [Fact]
    public async Task SyncAsync_RejectsDuplicatePluginNamesIgnoringCase()
    {
        await using var db = CreateDbContext();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _synchronizer.SyncAsync(
                db,
                [
                    new TestPlugin("ALI", ReportSubjectKind.Component),
                    new TestPlugin("ali", ReportSubjectKind.Radar)
                ]));

        Assert.Contains("piu plugin", exception.Message);
        Assert.Empty(db.ReportTypes);
    }

    private static RRDADbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RRDADbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new RRDADbContext(options);
    }

    private static ReportType CreateReportType(
        string key,
        string name,
        string description,
        ReportSubjectKind subjectKind) =>
        new()
        {
            Key = key,
            Name = name,
            Description = description,
            SubjectKind = subjectKind,
            Files = []
        };

    private sealed class TestPlugin(string name, ReportSubjectKind subjectKind) : IReportImporter
    {
        public string Name => name;
        public string Version => "1.0.0";
        public string SupportedFileExtension => ".test";
        public string MatchingPattern => "*";
        public ReportSubjectKind SubjectKind => subjectKind;
        public string SubjectKeyDefinedName => "Serial";

        public Task<bool> CanImportAsync(string fileName, Stream? validationConfigXml = null) =>
            Task.FromResult(true);

        public Task<ImportResult> ImportAsync(
            Stream fileStream,
            ValidationConfig config,
            IProgress<ImportProgress>? progress = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
