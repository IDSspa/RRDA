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
            culture: "it-IT");

        output.Position = 0;
        var document = XDocument.Load(output);
        var field = document.Descendants("Field")
            .Single(element => element.Attribute("definedName")?.Value == "Meas_F_1950");

        Assert.Equal("Meas_F_1950", field.Attribute("definedName")?.Value);
        Assert.Equal(expectedType, field.Attribute("type")?.Value);
        Assert.Equal("Serial", document.Root?.Attribute("subjectKeyField")?.Value);

        output.Position = 0;
        Assert.Equal("Serial", ValidationConfig.Load(output).SubjectKeyField);
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
