using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RRDA.Plugins.Common;

public static class OpenXmlExcelReader
{
    public static IEnumerable<IDictionary<string, string>> ReadRows(Stream stream)
    {
        using var doc = SpreadsheetDocument.Open(stream, false);
        var wbPart = doc.WorkbookPart!;
        var sheet = wbPart.Workbook.Sheets!.Elements<Sheet>().First();
        var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id!);

        var shared = wbPart.SharedStringTablePart?.SharedStringTable;
        var rows = wsPart.Worksheet.Descendants<Row>().ToList();
        if (rows.Count < 2) yield break;

        var headers = ReadHeaders(rows[0], shared);

        foreach (var row in rows.Skip(1))
        {
            var dict = new Dictionary<string, string>();

            foreach (var cell in row.Elements<Cell>())
            {
                var col = GetColumn(cell.CellReference!);
                if (!headers.TryGetValue(col, out var header)) continue;
                dict[header] = GetValue(cell, shared);
            }

            yield return dict;
        }
    }

    private static Dictionary<string, string> ReadHeaders(
        Row row, SharedStringTable? shared)
        => row.Elements<Cell>()
              .ToDictionary(
                  c => GetColumn(c.CellReference!),
                  c => GetValue(c, shared));

    private static string GetValue(Cell cell, SharedStringTable? shared)
    {
        if (cell.CellValue == null) return string.Empty;
        var v = cell.CellValue.InnerText;
        return cell.DataType?.Value == CellValues.SharedString
            ? shared?.ElementAt(int.Parse(v)).InnerText ?? string.Empty
            : v;
    }

    private static string GetColumn(string reference)
        => new(reference.Where(char.IsLetter).ToArray());
}