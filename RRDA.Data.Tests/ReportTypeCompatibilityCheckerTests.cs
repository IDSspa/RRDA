using Microsoft.EntityFrameworkCore;
using RRDA.Core;
using RRDA.Core.Validator;
using Xunit;

namespace RRDA.Data.Tests;

public sealed class ReportTypeCompatibilityCheckerTests
{
    private readonly IReportTypeCompatibilityChecker _checker = new ReportTypeCompatibilityChecker();

    [Fact]
    public async Task CheckAsync_ReturnsCompatibleForMatchingPlugin()
    {
        await using var db = CreateDbContext();
        db.ReportTypes.Add(CreateReportType("ALI", ReportSubjectKind.Component));
        await db.SaveChangesAsync();

        var result = await _checker.CheckAsync(
            db,
            [new TestPlugin("ALI", ReportSubjectKind.Component)]);

        Assert.True(result.IsCompatible);
        Assert.Empty(result.MissingReportTypes);
        Assert.Empty(result.SubjectKindMismatches);
    }

    [Fact]
    public async Task CheckAsync_ReportsMissingAndMismatchedPluginsWithoutWriting()
    {
        await using var db = CreateDbContext();
        db.ReportTypes.Add(CreateReportType("ALI", ReportSubjectKind.Component));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await _checker.CheckAsync(
            db,
            [
                new TestPlugin("ALI", ReportSubjectKind.Radar),
                new TestPlugin("NEW", ReportSubjectKind.SubAssembly)
            ]);

        Assert.False(result.IsCompatible);
        Assert.Equal(["NEW"], result.MissingReportTypes);

        var mismatch = Assert.Single(result.SubjectKindMismatches);
        Assert.Equal("ALI", mismatch.ReportTypeKey);
        Assert.Equal(ReportSubjectKind.Component, mismatch.DatabaseSubjectKind);
        Assert.Equal(ReportSubjectKind.Radar, mismatch.PluginSubjectKind);

        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal(1, await db.ReportTypes.CountAsync());
        Assert.Equal(ReportSubjectKind.Component, (await db.ReportTypes.SingleAsync()).SubjectKind);
    }

    [Fact]
    public async Task CheckAsync_MatchesReportTypeIgnoringCase()
    {
        await using var db = CreateDbContext();
        db.ReportTypes.Add(CreateReportType("ali", ReportSubjectKind.Component));
        await db.SaveChangesAsync();

        var result = await _checker.CheckAsync(
            db,
            [new TestPlugin("ALI", ReportSubjectKind.Component)]);

        Assert.True(result.IsCompatible);
    }

    [Fact]
    public async Task CheckAsync_RejectsDuplicatePluginNamesIgnoringCase()
    {
        await using var db = CreateDbContext();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _checker.CheckAsync(
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

    private static ReportType CreateReportType(string key, ReportSubjectKind subjectKind) =>
        new()
        {
            Key = key,
            Name = key,
            Description = string.Empty,
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
