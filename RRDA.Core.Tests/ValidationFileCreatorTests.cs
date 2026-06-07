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
    [InlineData("1950.002381", "double")]
    [InlineData("1950", "int")]
    [InlineData("testo", "string")]
    public void CreateFromStream_InfersTypeFromSharedStringValue(
        string sharedStringValue,
        string expectedType)
    {
        using var workbookStream = CreateWorkbookWithSharedString(
            "Meas_F_1950",
            sharedStringValue);
        using var output = new MemoryStream();

        ValidationFileCreator.CreateFromStream(
            workbookStream,
            output,
            "Serial",
            culture: "it-IT");

        output.Position = 0;
        var document = XDocument.Load(output);
        var field = document.Descendants("Field")
            .Single(element => element.Attribute("definedName")?.Value == "Meas_F_1950");

        Assert.Equal("Meas_F_1950", field.Attribute("definedName")?.Value);
        Assert.Equal(expectedType, field.Attribute("type")?.Value);
        Assert.Equal("Hz", field.Attribute("unit")?.Value);
        Assert.Equal("Serial", document.Root?.Attribute("subjectKeyField")?.Value);

        output.Position = 0;
        var config = ValidationConfig.Load(output);
        Assert.Equal("Serial", config.SubjectKeyField);
        Assert.Equal("Hz", config.FieldRules.Single(rule => rule.DefinedName == "Meas_F_1950").Unit);
    }

    [Theory]
    [InlineData("MeasuredTemperature", "°C")]
    [InlineData("Output_Voltage_Max", "V")]
    [InlineData("Signal_Gain", "dB")]
    [InlineData("Test_Duration", "s")]
    public void CreateFromStream_InfersUnitFromDefinedName(
        string definedName,
        string expectedUnit)
    {
        using var workbookStream = CreateWorkbookWithSharedString(definedName, "1.5");
        using var output = new MemoryStream();

        ValidationFileCreator.CreateFromStream(workbookStream, output, "Serial", culture: "it-IT");

        output.Position = 0;
        var field = XDocument.Load(output).Descendants("Field")
            .Single(element => element.Attribute("definedName")?.Value == definedName);

        Assert.Equal(expectedUnit, field.Attribute("unit")?.Value);
    }

    [Fact]
    public void CreateFromStream_OmitsUnitWhenDefinedNameHasNoKnownKeyword()
    {
        using var workbookStream = CreateWorkbookWithSharedString("Generic_Measurement", "1.5");
        using var output = new MemoryStream();

        ValidationFileCreator.CreateFromStream(workbookStream, output, "Serial", culture: "it-IT");

        output.Position = 0;
        var field = XDocument.Load(output).Descendants("Field")
            .Single(element => element.Attribute("definedName")?.Value == "Generic_Measurement");

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
}
