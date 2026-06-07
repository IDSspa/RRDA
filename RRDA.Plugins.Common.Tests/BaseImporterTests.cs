using RRDA.Core;
using RRDA.Core.Validator;
using Xunit;

namespace RRDA.Plugins.Common.Tests;

public sealed class BaseImporterTests
{
    [Fact]
    public async Task ImportAsync_UsesCustomImportDataAsyncAndKeepsCommonResultHandling()
    {
        var importer = new CustomImporter();

        var result = await importer.ImportAsync(
            Stream.Null,
            new ValidationConfig { SubjectKeyField = importer.SubjectKeyDefinedName });

        Assert.True(result.Success);
        Assert.Equal(importer.Name, result.ReportTypeKey);
        var entity = Assert.Single(result.Entities!);
        Assert.Equal("custom-value", entity.Properties["value"]);
    }

    [Fact]
    public async Task ImportAsync_RejectsCustomOutputWithoutValueProperty()
    {
        var importer = new InvalidCustomImporter();

        var result = await importer.ImportAsync(
            Stream.Null,
            new ValidationConfig { SubjectKeyField = importer.SubjectKeyDefinedName });

        Assert.False(result.Success);
        Assert.Null(result.Entities);
        Assert.Contains(result.Errors, error =>
            error.Contains("proprietà obbligatoria 'value'", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CanImportAsync_CanBeCustomizedWithoutReplacingImportAsync()
    {
        var importer = new CustomImporter();

        Assert.True(await importer.CanImportAsync("special.input"));
        Assert.False(await importer.CanImportAsync("ordinary.xlsx"));
    }

    [Fact]
    public async Task ImportAsync_RejectsValidatorWithDifferentSubjectKey()
    {
        var importer = new CustomImporter();

        var result = await importer.ImportAsync(
            Stream.Null,
            new ValidationConfig { SubjectKeyField = "DifferentKey" });

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error =>
            error.Contains(importer.SubjectKeyDefinedName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ImportAsync_RejectsPluginWithoutSubjectKey()
    {
        var importer = new MissingSubjectKeyImporter();

        var result = await importer.ImportAsync(
            Stream.Null,
            new ValidationConfig());

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error =>
            error.Contains("non dichiara", StringComparison.OrdinalIgnoreCase));
    }

    private class CustomImporter : BaseImporter
    {
        public override string Name => "CUSTOM";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".input";
        public override string MatchingPattern => "unused";
        public override ReportSubjectKind SubjectKind => ReportSubjectKind.Component;
        public override string SubjectKeyDefinedName => "Serial";

        public override Task<bool> CanImportAsync(string fileName, Stream? validationConfigXml = null) =>
            Task.FromResult(string.Equals(fileName, "special.input", StringComparison.OrdinalIgnoreCase));

        protected override Task<IEnumerable<ReportEntityDto>> ImportDataAsync(
            Stream xlsxStream,
            ValidationConfig config,
            IProgress<ImportProgress>? progress = null,
            CancellationToken ct = default) =>
            Task.FromResult<IEnumerable<ReportEntityDto>>(
            [
                new ReportEntityDto
                {
                    EntityKind = "Custom",
                    Key = "CustomKey",
                    Properties = new Dictionary<string, string>
                    {
                        ["value"] = "custom-value",
                        ["data_type"] = "string"
                    }
                }
            ]);
    }

    private sealed class InvalidCustomImporter : CustomImporter
    {
        protected override Task<IEnumerable<ReportEntityDto>> ImportDataAsync(
            Stream xlsxStream,
            ValidationConfig config,
            IProgress<ImportProgress>? progress = null,
            CancellationToken ct = default) =>
            Task.FromResult<IEnumerable<ReportEntityDto>>(
            [
                new ReportEntityDto
                {
                    EntityKind = "Custom",
                    Key = "Invalid",
                    Properties = []
                }
            ]);
    }

    private sealed class MissingSubjectKeyImporter : CustomImporter
    {
        public override string SubjectKeyDefinedName => string.Empty;
    }
}
