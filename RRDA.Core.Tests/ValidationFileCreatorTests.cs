using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using RRDA.Core.Validator;
using System.Xml.Linq;
using Xunit;

namespace RRDA.Core.Tests;

public sealed class ValidationFileCreatorTests
{
    [Theory]
    [InlineData("1950.002381", "double", "MHz")]
    [InlineData("1950", "int", "MHz")]
    [InlineData("testo", "string", null)]
    public void CreateFromStream_InfersTypeFromSharedStringValue(
        string sharedStringValue,
        string expectedType,
        string? expectedUnit)
    {
        using var mappings = CreateUnitMappings();
        using var workbookStream = CreateWorkbookWithSharedString("Meas_F_1950", sharedStringValue);
        using var output = new MemoryStream();

        ValidationFileCreator.CreateFromStream(
            workbookStream,
            output,
            "Serial",
            mappings.Path,
            culture: "it-IT");

        output.Position = 0;
        var document = XDocument.Load(output);
        var field = document.Descendants("Field")
            .Single(element => element.Attribute("definedName")?.Value == "Meas_F_1950");

        Assert.Equal("Meas_F_1950", field.Attribute("definedName")?.Value);
        Assert.Equal(expectedType, field.Attribute("type")?.Value);
        Assert.Equal(expectedUnit, field.Attribute("unit")?.Value);
        Assert.Equal("Serial", document.Root?.Attribute("subjectKeyField")?.Value);

        output.Position = 0;
        var config = ValidationConfig.Load(output);
        Assert.Equal("Serial", config.SubjectKeyField);
        Assert.Equal(expectedUnit, config.FieldRules.Single(rule => rule.DefinedName == "Meas_F_1950").Unit);
    }

    [Theory]
    [InlineData("Meas_F_1950", "MHz")]
    [InlineData("PWR_3_3", "A")]
    [InlineData("RIPPLE_V_DIGITAL", "mV")]
    [InlineData("MAN2_PWR_PA_FLAT", "dB")]
    [InlineData("meas_f_1950", "MHz")]
    public void CreateFromStream_InfersUnitFromDefinedName(
        string definedName,
        string expectedUnit)
    {
        using var mappings = CreateUnitMappings();
        using var workbookStream = CreateWorkbookWithSharedString(definedName, "1.5");
        using var output = new MemoryStream();

        ValidationFileCreator.CreateFromStream(
            workbookStream,
            output,
            "Serial",
            mappings.Path,
            culture: "it-IT");

        output.Position = 0;
        var field = XDocument.Load(output).Descendants("Field")
            .Single(element => element.Attribute("definedName")?.Value == definedName);

        Assert.Equal(expectedUnit, field.Attribute("unit")?.Value);
    }

    [Fact]
    public void CreateFromStream_OmitsUnitWhenNoMappingMatches()
    {
        using var mappings = CreateUnitMappings();
        using var workbookStream = CreateWorkbookWithSharedString("Generic_Measurement", "1.5");
        using var output = new MemoryStream();

        ValidationFileCreator.CreateFromStream(
            workbookStream,
            output,
            "Serial",
            mappings.Path,
            culture: "it-IT");

        output.Position = 0;
        var field = XDocument.Load(output).Descendants("Field")
            .Single(element => element.Attribute("definedName")?.Value == "Generic_Measurement");

        Assert.Null(field.Attribute("unit"));
    }

    [Theory]
    [InlineData("ALI_PWR_IN", "123.45", "double", "W")]
    [InlineData("ALI_PWR_IN_CHECK", "PASS", "string", null)]
    public void CreateFromStream_AppliesUnitsOnlyToNumericFields(
        string definedName,
        string value,
        string expectedType,
        string? expectedUnit)
    {
        using var workbookStream = CreateWorkbookWithSharedString(definedName, value);
        using var output = new MemoryStream();

        ValidationFileCreator.CreateFromStream(
            workbookStream,
            output,
            "Serial",
            Path.Combine(AppContext.BaseDirectory, "UnitMappings.xml"),
            culture: "it-IT");

        output.Position = 0;
        var field = XDocument.Load(output).Descendants("Field")
            .Single(element => element.Attribute("definedName")?.Value == definedName);

        Assert.Equal(expectedType, field.Attribute("type")?.Value);
        Assert.Equal(expectedUnit, field.Attribute("unit")?.Value);
    }

    [Fact]
    public void CreateFromStream_OmitsUnitsWhenMappingPathIsEmpty()
    {
        using var workbookStream = CreateWorkbookWithSharedString("Meas_F_1950", "1.5");
        using var output = new MemoryStream();

        ValidationFileCreator.CreateFromStream(workbookStream, output, "Serial");

        output.Position = 0;
        var field = XDocument.Load(output).Descendants("Field")
            .Single(element => element.Attribute("definedName")?.Value == "Meas_F_1950");

        Assert.Null(field.Attribute("unit"));
    }

    [Fact]
    public void CreateFromStream_RejectsMissingPluginSubjectKey()
    {
        using var workbookStream = CreateWorkbookWithSharedString("Measurement", "1.5");
        using var output = new MemoryStream();

        var exception = Assert.Throws<InvalidDataException>(() =>
            ValidationFileCreator.CreateFromStream(workbookStream, output, "MissingKey"));

        Assert.Contains("MissingKey", exception.Message);
    }

    [Fact]
    public void UnitMappingResolver_RejectsInvalidXml()
    {
        using var mappings = new TemporaryFile("<UnitMappings />");

        Assert.Throws<InvalidDataException>(() => UnitMappingResolver.Load(mappings.Path));
    }

    [Theory]
    [InlineData("Meas_F_1950", "MHz")]
    [InlineData("RIPPLE_V_DIGITAL", "mV")]
    [InlineData("MAN2_PWR_PA_FLAT", "dB")]
    [InlineData("PWR_3_3", "A")]
    [InlineData("ALI_PWR_IN", "W")]
    public void UnitMappingResolver_LoadsDistributedMappingFile(
        string definedName,
        string expectedUnit)
    {
        var resolver = UnitMappingResolver.Load(
            Path.Combine(AppContext.BaseDirectory, "UnitMappings.xml"));

        Assert.Equal(expectedUnit, resolver.InferUnit(definedName));
    }

    private static TemporaryFile CreateUnitMappings()
    {
        const string xml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <UnitMappings version="1"
                          matchMode="glob"
                          caseSensitive="false"
                          resolution="highestPriorityThenDocumentOrder">
              <Rule unit="mV" priority="300">
                <Pattern>RIPPLE_V_*</Pattern>
              </Rule>
              <Rule unit="dB" priority="300">
                <Pattern>MAN*_PWR_PA_FLAT</Pattern>
              </Rule>
              <Rule unit="A" priority="300">
                <Pattern>PWR_3_3</Pattern>
              </Rule>
              <Rule unit="mVpp" priority="100">
                <Pattern>Ripple_*</Pattern>
              </Rule>
              <Rule unit="dBm" priority="100">
                <Pattern>MAN*_PWR_*</Pattern>
              </Rule>
              <Rule unit="MHz" priority="100">
                <Pattern>Meas_F_*</Pattern>
              </Rule>
            </UnitMappings>
            """;

        return new TemporaryFile(xml);
    }

    private static MemoryStream CreateWorkbookWithSharedString(
        string definedName,
        string value)
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
                            CellReference = "K54",
                            DataType = CellValues.SharedString,
                            CellValue = new CellValue("0")
                        },
                        new Cell
                        {
                            CellReference = "L54",
                            DataType = CellValues.Number,
                            CellValue = new CellValue("1")
                        })));

            workbookPart.Workbook.AppendChild(new Sheets(
                new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1,
                    Name = "Procedura"
                }));
            workbookPart.Workbook.AppendChild(new DefinedNames(
                new DefinedName
                {
                    Name = definedName,
                    Text = "Procedura!$K$54"
                },
                new DefinedName
                {
                    Name = "Serial",
                    Text = "Procedura!$L$54"
                }));
        }

        stream.Position = 0;
        return stream;
    }

    private sealed class TemporaryFile : IDisposable
    {
        public TemporaryFile(string content)
        {
            Path = System.IO.Path.GetTempFileName();
            File.WriteAllText(Path, content);
        }

        public string Path { get; }

        public void Dispose()
        {
            File.Delete(Path);
        }
    }
}
