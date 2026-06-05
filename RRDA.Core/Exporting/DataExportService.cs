using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace RRDA.Core.Exporting
{
    public enum DataExportFormat
    {
        Csv,
        Excel
    }

    public sealed record DataExportColumn(string Key, string Header);

    public sealed record DataExportTable(
        IReadOnlyCollection<DataExportColumn> Columns,
        IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Rows);

    public sealed record DataExportDocument(
        byte[] Content,
        string ContentType,
        string FileExtension);

    public interface IDataExportService
    {
        DataExportDocument Export(DataExportTable table, DataExportFormat format);
    }

    public sealed class DataExportService : IDataExportService
    {
        public DataExportDocument Export(DataExportTable table, DataExportFormat format)
        {
            ArgumentNullException.ThrowIfNull(table);

            return format switch
            {
                DataExportFormat.Csv => ExportCsv(table),
                DataExportFormat.Excel => ExportExcel(table),
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
            };
        }

        private static DataExportDocument ExportCsv(DataExportTable table)
        {
            var content = BuildDelimitedContent(table, ",");
            var bytes = Encoding.UTF8.GetPreamble()
                .Concat(Encoding.UTF8.GetBytes(content))
                .ToArray();

            return new DataExportDocument(bytes, "text/csv", ".csv");
        }

        private static DataExportDocument ExportExcel(DataExportTable table)
        {
            using var stream = new MemoryStream();
            using (var document = SpreadsheetDocument.Create(
                       stream,
                       SpreadsheetDocumentType.Workbook,
                       autoSave: true))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var sheetData = new SheetData();
                worksheetPart.Worksheet = new Worksheet(sheetData);

                sheetData.Append(CreateExcelRow(table.Columns.Select(column => column.Header)));
                foreach (var row in table.Rows)
                {
                    sheetData.Append(CreateExcelRow(table.Columns.Select(column =>
                        row.TryGetValue(column.Key, out var value) ? value : null)));
                }

                var sheets = workbookPart.Workbook.AppendChild(new Sheets());
                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1,
                    Name = "Data"
                });
            }

            return new DataExportDocument(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".xlsx");
        }

        private static Row CreateExcelRow(IEnumerable<object?> values)
        {
            var row = new Row();
            foreach (var value in values)
                row.Append(CreateExcelCell(value));

            return row;
        }

        private static Cell CreateExcelCell(object? value)
        {
            return value switch
            {
                null => CreateInlineStringCell(string.Empty),
                byte or sbyte or short or ushort or int or uint or long or ulong
                    or float or double or decimal => new Cell
                    {
                        DataType = CellValues.Number,
                        CellValue = new CellValue(FormatValue(value))
                    },
                bool boolean => new Cell
                {
                    DataType = CellValues.Boolean,
                    CellValue = new CellValue(boolean ? "1" : "0")
                },
                _ => CreateInlineStringCell(FormatValue(value))
            };
        }

        private static Cell CreateInlineStringCell(string value)
        {
            return new Cell
            {
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new Text(value)
                {
                    Space = SpaceProcessingModeValues.Preserve
                })
            };
        }

        private static string BuildDelimitedContent(DataExportTable table, string separator)
        {
            var builder = new StringBuilder();
            builder.AppendLine(BuildRow(table.Columns.Select(column => column.Header), separator));

            foreach (var row in table.Rows)
            {
                builder.AppendLine(BuildRow(
                    table.Columns.Select(column =>
                        row.TryGetValue(column.Key, out var value)
                            ? FormatValue(value)
                            : string.Empty),
                    separator));
            }

            return builder.ToString();
        }

        private static string BuildRow(IEnumerable<string> fields, string separator)
        {
            return string.Join(
                separator,
                fields.Select(field => $"\"{field.Replace("\"", "\"\"")}\""));
        }

        private static string FormatValue(object? value)
        {
            return value switch
            {
                null => string.Empty,
                DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                DateTimeOffset dateTimeOffset => dateTimeOffset.ToString(
                    "yyyy-MM-dd HH:mm",
                    CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture)
                    ?? string.Empty,
                _ => value.ToString() ?? string.Empty
            };
        }
    }
}
