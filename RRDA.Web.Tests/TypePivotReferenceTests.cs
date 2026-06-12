using Microsoft.EntityFrameworkCore;
using RRDA.Core;
using RRDA.Data;
using RRDA.Web.Areas.Data.Controllers;
using RRDA.Web.Services;
using RRDA.Web.Services.TypePivot;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace RRDA.Web.Tests;

public sealed class TypePivotReferenceTests
{
    [Fact]
    public async Task Dataset_IncludesReferenceColumnsButExcludesThemFromStatistics()
    {
        await using var db = CreateDbContext();
        var radarType = CreateReportType("RADAR", ReportSubjectKind.Radar);
        var aliType = CreateReportType("ALI", ReportSubjectKind.Component);
        var radarFile = CreateReportFile(radarType, "radar.xlsx");
        var referenceEntity = AddEntity(radarFile, "SN_ALI", "522", "int");
        AddEntity(radarFile, "Power", "12.5", "double");
        db.ReportReferences.Add(new ReportReference
        {
            SourceReportFile = radarFile,
            SourceReportEntity = referenceEntity,
            TargetReportType = aliType,
            TargetKeyField = "Serial",
            TargetKeyValue = "522",
            Origin = ReportReferenceOrigin.Imported,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var dataset = await new TypePivotDatasetService(db).GetAsync(
            new TypePivotFilterRequest(radarType.Id, null, null, null, null, null, null, null, null));

        Assert.NotNull(dataset);
        Assert.Contains("SN_ALI", dataset.Metadata.VisibleHeaders);
        Assert.Contains("SN_ALI", dataset.Metadata.ReferenceHeaders);
        Assert.DoesNotContain("SN_ALI", dataset.Metadata.StatisticalHeaders);
        Assert.Contains("Power", dataset.Metadata.VisibleHeaders);
        Assert.Contains("Power", dataset.Metadata.StatisticalHeaders);
    }

    [Theory]
    [InlineData("522", "0522")]
    [InlineData(" 522 ", "0522")]
    [InlineData("42543-095", "42543-095")]
    public void ReferenceKeyComparer_MatchesEquivalentKeys(string reference, string subjectKey)
    {
        Assert.True(ReportReferenceKeyComparer.Equals(reference, subjectKey));
    }

    [Fact]
    public async Task Statistics_IgnoreNonFiniteValues()
    {
        await using var db = CreateDbContext();
        var reportType = CreateReportType("RADAR", ReportSubjectKind.Radar);
        var reportFile = CreateReportFile(reportType, "radar.xlsx");
        AddEntity(reportFile, "Balance", "NaN", "double");
        AddEntity(reportFile, "Balance", "12.5", "double");
        db.ReportFiles.Add(reportFile);
        await db.SaveChangesAsync();

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var statistics = await new TypePivotStatisticsService(db, cache).GetAsync(
            [reportFile.Id],
            ["Balance"]);

        var balance = Assert.IsType<TypePivotColumnStatistics>(statistics["Balance"]);
        Assert.Equal(1, balance.Count);
        Assert.Equal(12.5, balance.Mean);
        Assert.Equal(12.5, balance.Min);
        Assert.Equal(12.5, balance.Max);
    }

    [Fact]
    public async Task ViewModelBuilder_ResolvesReferenceTargetForNavigation()
    {
        await using var db = CreateDbContext();
        var radarType = CreateReportType("RADAR", ReportSubjectKind.Radar);
        var aliType = CreateReportType("ALI", ReportSubjectKind.Component);
        var radarFile = CreateReportFile(radarType, "radar.xlsx");
        var referenceEntity = AddEntity(radarFile, "SN_ALI", "522", "int");
        var aliFile = CreateReportFile(aliType, "ali-0522.xlsx");
        AddEntity(aliFile, "Serial", "0522", "int", isSubjectKey: true);
        db.ReportFiles.AddRange(radarFile, aliFile);
        db.ReportReferences.Add(new ReportReference
        {
            SourceReportFile = radarFile,
            SourceReportEntity = referenceEntity,
            TargetReportType = aliType,
            TargetKeyField = "Serial",
            TargetKeyValue = "522",
            Origin = ReportReferenceOrigin.Imported,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var builder = new TypePivotViewModelBuilder(
            db,
            new TypePivotDatasetService(db),
            new TypePivotOrderingService(db));
        var model = await builder.BuildAsync(new TypePivotViewRequest(
            new TypePivotFilterRequest(radarType.Id, null, null, null, null, null, null, null, null),
            null,
            null,
            1,
            50,
            4,
            true));

        var row = Assert.Single(model!.Rows);
        Assert.Equal(aliFile.Id, row.ReferenceTargetFileIds["SN_ALI"]);
        Assert.Equal("522", row.Values["SN_ALI"]);
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
            SubjectKind = subjectKind,
            Files = []
        };

    private static ReportFile CreateReportFile(ReportType reportType, string fileName) =>
        new()
        {
            FileName = fileName,
            FullPath = fileName,
            UploadedAt = DateTime.UtcNow,
            ReportType = reportType,
            Entities = [],
            FileLastModify = DateTime.UtcNow
        };

    private static ReportEntity AddEntity(
        ReportFile file,
        string key,
        string value,
        string dataType,
        bool isSubjectKey = false)
    {
        var entity = new ReportEntity
        {
            ReportFile = file,
            ReportSheet = "Sheet1",
            Key = key,
            Properties = []
        };
        entity.Properties.Add(new ReportProperty
        {
            ReportEntity = entity,
            Name = "value",
            Value = value,
            DataType = dataType,
            IsSubjectKey = isSubjectKey
        });
        file.Entities.Add(entity);
        return entity;
    }
}
