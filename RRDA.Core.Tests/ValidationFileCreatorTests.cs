using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using RRDA.Core.Validator;
using RRDA.Core;
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

    [Fact]
    public void CreateFromStream_AppliesPluginReferenceDefinition()
    {
        using var workbookStream = CreateWorkbookWithSharedString("SN_ALI", "12345");
        using var output = new MemoryStream();

        ValidationFileCreator.CreateFromStream(
            workbookStream,
            output,
            "Serial",
            referenceDefinitions:
            [
                new ReportReferenceDefinition("SN_ALI", "ALI", "Serial")
            ]);

        output.Position = 0;
        var field = XDocument.Load(output).Descendants("Field")
            .Single(element => element.Attribute("definedName")?.Value == "SN_ALI");

        Assert.Equal("ALI", field.Attribute("referenceReportType")?.Value);
        Assert.Equal("Serial", field.Attribute("referenceKeyField")?.Value);
    }

    [Fact]
    public void CreateFromStream_RejectsMissingPluginReferenceDefinedName()
    {
        using var workbookStream = CreateWorkbookWithSharedString("Measurement", "1.5");
        using var output = new MemoryStream();

        var exception = Assert.Throws<InvalidDataException>(() =>
            ValidationFileCreator.CreateFromStream(
                workbookStream,
                output,
                "Serial",
                referenceDefinitions:
                [
                    new ReportReferenceDefinition("SN_ALI", "ALI", "Serial")
                ]));

        Assert.Contains("SN_ALI", exception.Message);
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
    public void CreateFromStream_ExcludesDefinedNamesFromImportBanList()
    {
        using var banList = CreateImportBanList(
            definedNamePatterns: ["Generic_*"]);
        using var workbookStream = CreateWorkbookWithSharedString("Generic_Measurement", "1.5");
        using var output = new MemoryStream();

        ValidationFileCreator.CreateFromStream(
            workbookStream,
            output,
            "Serial",
            importBanListPath: banList.Path);

        output.Position = 0;
        var document = XDocument.Load(output);

        Assert.DoesNotContain(
            document.Descendants("Field"),
            element => element.Attribute("definedName")?.Value == "Generic_Measurement");
        Assert.Contains(
            document.Descendants("Field"),
            element => element.Attribute("definedName")?.Value == "Serial");
    }

    [Fact]
    public void CreateFromStream_ExcludesSheetsAndTheirDefinedNamesFromImportBanList()
    {
        using var banList = CreateImportBanList(sheetPatterns: ["Debug*"]);
        using var workbookStream = CreateWorkbookWithSharedString(
            "DebugValue",
            "1.5",
            definedNameSheet: "DebugData");
        using var output = new MemoryStream();

        ValidationFileCreator.CreateFromStream(
            workbookStream,
            output,
            "Serial",
            importBanListPath: banList.Path);

        output.Position = 0;
        var document = XDocument.Load(output);

        Assert.DoesNotContain(
            document.Descendants("Sheet"),
            element => element.Attribute("Name")?.Value == "DebugData");
        Assert.DoesNotContain(
            document.Descendants("Field"),
            element => element.Attribute("definedName")?.Value == "DebugValue");
    }

    [Fact]
    public void CreateFromStream_RejectsSubjectKeyExcludedByImportBanList()
    {
        using var banList = CreateImportBanList(definedNamePatterns: ["Serial"]);
        using var workbookStream = CreateWorkbookWithSharedString("Measurement", "1.5");
        using var output = new MemoryStream();

        var exception = Assert.Throws<InvalidDataException>(() =>
            ValidationFileCreator.CreateFromStream(
                workbookStream,
                output,
                "Serial",
                importBanListPath: banList.Path));

        Assert.Contains("banlist", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateFromStream_RejectsSubjectKeyOnExcludedSheet()
    {
        using var banList = CreateImportBanList(sheetPatterns: ["Procedura"]);
        using var workbookStream = CreateWorkbookWithSharedString("Measurement", "1.5");
        using var output = new MemoryStream();

        var exception = Assert.Throws<InvalidDataException>(() =>
            ValidationFileCreator.CreateFromStream(
                workbookStream,
                output,
                "Serial",
                importBanListPath: banList.Path));

        Assert.Contains("banlist", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportBanListResolver_RejectsInvalidXml()
    {
        using var banList = new TemporaryFile("<ImportBanList />");

        Assert.Throws<InvalidDataException>(() => ImportBanListResolver.Load(banList.Path));
    }

    [Fact]
    public void ImportBanListResolver_LoadsDistributedExcelExclusions()
    {
        var resolver = ImportBanListResolver.Load(
            Path.Combine(AppContext.BaseDirectory, "ImportBanList.xml"));

        Assert.False(resolver.IsDefinedNameExcluded("Measurement"));
        Assert.False(resolver.IsSheetExcluded("Procedura"));
        Assert.True(resolver.IsDefinedNameExcluded("_xlnm.Print_Area"));
        Assert.True(resolver.IsDefinedNameExcluded("__xlnm.Print_Titles"));
        Assert.True(resolver.IsDefinedNameExcluded("_xl.InternalName"));
        Assert.True(resolver.IsDefinedNameExcluded("__xl.InternalName"));
        Assert.True(resolver.IsDefinedNameExcluded("Print_Area"));
        Assert.True(resolver.IsDefinedNameExcluded("Print_Titles"));
        Assert.True(resolver.IsDefinedNameExcluded("Filter_Database"));
        Assert.True(resolver.IsDefinedNameExcluded("_FilterDatabase"));
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

    private static TemporaryFile CreateImportBanList(
        IReadOnlyList<string>? definedNamePatterns = null,
        IReadOnlyList<string>? sheetPatterns = null)
    {
        var root = new XElement(
            "ImportBanList",
            new XAttribute("version", "1"),
            new XAttribute("matchMode", "glob"),
            new XAttribute("caseSensitive", "false"));

        if (definedNamePatterns is { Count: > 0 })
        {
            root.Add(new XElement(
                "DefinedNames",
                definedNamePatterns.Select(pattern => new XElement("Pattern", pattern))));
        }

        if (sheetPatterns is { Count: > 0 })
        {
            root.Add(new XElement(
                "Sheets",
                sheetPatterns.Select(pattern => new XElement("Pattern", pattern))));
        }

        return new TemporaryFile(new XDocument(root).ToString());
    }

    private static MemoryStream CreateWorkbookWithSharedString(
        string definedName,
        string value,
        string definedNameSheet = "Procedura")
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

            var definedNameWorksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            definedNameWorksheetPart.Worksheet = new Worksheet(
                new SheetData(
                    new Row(
                        new Cell
                        {
                            CellReference = "K54",
                            DataType = CellValues.SharedString,
                            CellValue = new CellValue("0")
                        })));

            var subjectKeyWorksheetPart = definedNameSheet == "Procedura"
                ? definedNameWorksheetPart
                : workbookPart.AddNewPart<WorksheetPart>();
            if (subjectKeyWorksheetPart != definedNameWorksheetPart)
            {
                subjectKeyWorksheetPart.Worksheet = new Worksheet(
                    new SheetData(
                        new Row(
                        new Cell
                        {
                            CellReference = "L54",
                            DataType = CellValues.Number,
                            CellValue = new CellValue("1")
                        })));
            }
            else
            {
                definedNameWorksheetPart.Worksheet.Descendants<Row>().Single().Append(
                    new Cell
                    {
                        CellReference = "L54",
                        DataType = CellValues.Number,
                        CellValue = new CellValue("1")
                    });
            }

            var sheets = new Sheets(
                new Sheet
                {
                    Id = workbookPart.GetIdOfPart(definedNameWorksheetPart),
                    SheetId = 1,
                    Name = definedNameSheet
                });
            if (subjectKeyWorksheetPart != definedNameWorksheetPart)
            {
                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(subjectKeyWorksheetPart),
                    SheetId = 2,
                    Name = "Procedura"
                });
            }
            workbookPart.Workbook.AppendChild(sheets);
            workbookPart.Workbook.AppendChild(new DefinedNames(
                new DefinedName
                {
                    Name = definedName,
                    Text = $"'{definedNameSheet}'!$K$54"
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
