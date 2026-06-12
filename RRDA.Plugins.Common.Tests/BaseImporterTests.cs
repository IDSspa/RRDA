using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
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

    [Fact]
    public async Task ImportAsync_PropagatesReferenceMetadataAndValue()
    {
        var importer = new ReferenceImporter();
        var config = new ValidationConfig
        {
            SubjectKeyField = importer.SubjectKeyDefinedName,
            Sheets = [new Sheet { Name = "Procedura" }],
            FieldRules =
            [
                new FieldRule
                {
                    DefinedName = "SN_ALI",
                    ReferenceReportType = "ALI",
                    ReferenceKeyField = "Serial"
                }
            ]
        };
        using var workbook = CreateWorkbook("SN_ALI", "12345");

        var result = await importer.ImportAsync(workbook, config);

        var reference = Assert.Single(result.Entities!).Reference;
        Assert.NotNull(reference);
        Assert.Equal("ALI", reference.TargetReportTypeKey);
        Assert.Equal("Serial", reference.TargetKeyField);
        Assert.Equal("12345", reference.TargetKeyValue);
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

    private sealed class ReferenceImporter : BaseImporter
    {
        public override string Name => "RADAR";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override string MatchingPattern => "RADAR";
        public override ReportSubjectKind SubjectKind => ReportSubjectKind.Radar;
        public override string SubjectKeyDefinedName => "Serial";
    }

    private static MemoryStream CreateWorkbook(string definedName, string value)
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(
                   stream,
                   SpreadsheetDocumentType.Workbook,
                   autoSave: true))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var sharedStringsPart = workbookPart.AddNewPart<SharedStringTablePart>();
            sharedStringsPart.SharedStringTable = new SharedStringTable(
                new SharedStringItem(new Text(value)));
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new Worksheet(
                new SheetData(
                    new Row(
                        new Cell
                        {
                            CellReference = "A1",
                            DataType = CellValues.SharedString,
                            CellValue = new CellValue("0")
                        },
                        new Cell
                        {
                            CellReference = "A2",
                            DataType = CellValues.Number,
                            CellValue = new CellValue("1")
                        })));

            workbookPart.Workbook.Append(new Sheets(
                new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1,
                    Name = "Procedura"
                }));
            workbookPart.Workbook.Append(new DefinedNames(
                new DefinedName { Name = definedName, Text = "Procedura!$A$1" },
                new DefinedName { Name = "Serial", Text = "Procedura!$A$2" }));
        }

        stream.Position = 0;
        return stream;
    }
}
