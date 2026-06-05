using System.Text;
using DocumentFormat.OpenXml.Packaging;
using RRDA.Core.Exporting;
using Xunit;

namespace RRDA.Core.Tests;

public sealed class DataExportServiceTests
{
    private readonly IDataExportService _service = new DataExportService();

    [Fact]
    public void ExportCsv_UsesColumnOrderAndEscapesValues()
    {
        var table = new DataExportTable(
            [
                new DataExportColumn("name", "Name"),
                new DataExportColumn("notes", "Notes")
            ],
            [
                new Dictionary<string, object?>
                {
                    ["notes"] = "quoted \"value\"",
                    ["name"] = "alpha,beta"
                }
            ]);

        var result = _service.Export(table, DataExportFormat.Csv);

        Assert.Equal("text/csv", result.ContentType);
        Assert.Equal(".csv", result.FileExtension);
        Assert.True(result.Content.Take(Encoding.UTF8.GetPreamble().Length)
            .SequenceEqual(Encoding.UTF8.GetPreamble()));
        Assert.Equal(
            "\"Name\",\"Notes\"\r\n\"alpha,beta\",\"quoted \"\"value\"\"\"\r\n",
            ReadContent(result));
    }

    [Fact]
    public void ExportExcel_CreatesNativeWorkbookWithOrderedValues()
    {
        var table = new DataExportTable(
            [
                new DataExportColumn("date", "Date"),
                new DataExportColumn("number", "Number"),
                new DataExportColumn("missing", "Missing")
            ],
            [
                new Dictionary<string, object?>
                {
                    ["date"] = new DateTime(2026, 6, 5, 14, 30, 0),
                    ["number"] = 12.5
                }
            ]);

        var result = _service.Export(table, DataExportFormat.Excel);

        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            result.ContentType);
        Assert.Equal(".xlsx", result.FileExtension);

        using var stream = new MemoryStream(result.Content);
        using var workbook = SpreadsheetDocument.Open(stream, false);
        var worksheet = workbook.WorkbookPart!.WorksheetParts.Single().Worksheet;
        Assert.NotNull(worksheet);
        var rows = worksheet
            .Descendants<DocumentFormat.OpenXml.Spreadsheet.Row>()
            .ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(["Date", "Number", "Missing"], rows[0].ChildElements
            .Select(cell => cell.InnerText)
            .ToArray());
        Assert.Equal(["2026-06-05 14:30", "12.5", ""], rows[1].ChildElements
            .Select(cell => cell.InnerText)
            .ToArray());
    }

    private static string ReadContent(DataExportDocument document)
    {
        return Encoding.UTF8.GetString(document.Content.AsSpan(Encoding.UTF8.GetPreamble().Length));
    }
}
