using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

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
    // Overload che legge solo i fogli indicati nella configurazione
    public static IEnumerable<IDictionary<string, string>> ReadRows(Stream stream, IEnumerable<Sheet> sheets)
    {
        using var doc = SpreadsheetDocument.Open(stream, false);
        var wbPart = doc.WorkbookPart!;
        var workbookSheets = wbPart.Workbook.Sheets!.Elements<Sheet>()
                               .Where(s => s.Name != null)
                               .ToDictionary(s => s.Name!.Value, s => s);

        var shared = wbPart.SharedStringTablePart?.SharedStringTable;

        foreach (var cfgSheet in sheets)
        {
            var cfgName = cfgSheet?.Name?.Value;
            if (string.IsNullOrEmpty(cfgName)) continue;

            if (!workbookSheets.TryGetValue(cfgName, out var workbookSheet)) continue;

            var wsPart = (WorksheetPart)wbPart.GetPartById(workbookSheet.Id!);
            var rows = wsPart.Worksheet.Descendants<Row>().ToList();
            if (rows.Count < 2) continue;
            
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
    }
    /// <summary>
    /// Legge il valore testuale di una singola cella identificata dal suo indirizzo (es: "B5").
    /// Gestisce SharedString, valori numerici, date e testo inline.
    /// </summary>
    public static string ReadCellValue(WorksheetPart wsPart, string cellAddress, SharedStringTable? sharedStrings, WorkbookPart wbPart)
    {
        // Cerca la cella nel worksheet
        var cell = wsPart.Worksheet
                         .Descendants<Cell>()
                         .FirstOrDefault(c => string.Equals(
                             NormalizeCellRef(c.CellReference?.Value ?? ""),
                             cellAddress.ToUpperInvariant(),
                             StringComparison.OrdinalIgnoreCase));

        if (cell?.CellValue == null)
            return string.Empty;

        var rawValue = cell.CellValue.InnerText;

        // SharedString
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (int.TryParse(rawValue, out var idx) && sharedStrings != null)
                return sharedStrings.ElementAt(idx).InnerText;

            return rawValue;
        }

        // Data (numero seriale Excel) → converti in stringa ISO
        if (cell.DataType == null || cell.DataType.Value == CellValues.Number)
        {
            var styleIdx = (int)(cell.StyleIndex?.Value ?? 0);
            if (IsDateFormat(wbPart, styleIdx) && double.TryParse(
                    rawValue,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var serial))
            {
                var dt = DateTime.FromOADate(serial);
                return dt.ToString("yyyy-MM-dd");
            }
        }

        return rawValue;
    }
    // =========================================================================
    // Helper – lettura/navigazione OpenXML
    // =========================================================================
    /// <summary>Costruisce un dizionario sheetName → WorksheetPart.</summary>
    public static Dictionary<string, WorksheetPart> BuildSheetIndex(Workbook workbook, WorkbookPart wbPart)
    {
        var index = new Dictionary<string, WorksheetPart>(StringComparer.OrdinalIgnoreCase);

        foreach (Sheet sheet in workbook.Sheets?.Elements<Sheet>()
                                         ?? Enumerable.Empty<Sheet>())
        {
            if (sheet.Name is null || sheet.Id?.Value is null)
                continue;

            if (wbPart.GetPartById(sheet.Id.Value) is WorksheetPart wsPart)
                index[sheet.Name.Value!] = wsPart;
        }

        return index;
    }
    /// <summary>
    /// Costruisce un dizionario  definedName → (sheetName, cellAddress)
    /// a partire dai DefinedNames della workbook.
    /// Un DefinedName ha tipicamente la forma  SheetName!$COL$ROW  oppure
    /// SheetName!$COL$ROW:$COL2$ROW2  (per i range).
    /// Restituiamo solo l'indirizzo della prima cella (angolo in alto a sinistra).
    /// </summary>
    public static Dictionary<string, (string SheetName, string CellAddress)> BuildDefinedNamesIndex(Workbook workbook)
    {
        var index = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

        var definedNames = workbook.DefinedNames?.Elements<DefinedName>()
                           ?? Enumerable.Empty<DefinedName>();

        foreach (var dn in definedNames)
        {
            var name = dn.Name?.Value;
            var formula = dn.Text; // es: "Prima_Pagina!$B$5" oppure "Prima_Pagina!$B$5:$D$10"

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(formula))
                continue;

            // Prende solo la prima cella del range (prima del ':')
            var firstPart = formula.Split(':')[0].Trim();

            // Separa sheetName da cellAddress sul carattere '!'
            var bangIdx = firstPart.LastIndexOf('!');
            if (bangIdx < 0)
                continue;

            var rawSheet = firstPart[..bangIdx].Trim('\''); // rimuove apici eventualmente presenti
            var rawCell = firstPart[(bangIdx + 1)..].Replace("$", ""); // rimuove i '$'

            index[name] = (rawSheet, rawCell);
        }

        return index;
    }
    // =========================================================================
    // Helper – coordinate cella
    // =========================================================================
    /// <summary>
    /// Analizza un indirizzo di cella (es: "B5") e restituisce
    /// (rowIndex 1-based, colIndex 1-based).
    /// </summary>
    public static (int Row, int Col) ParseCellReference(string cellRef)
    {
        var colStr = new string(cellRef.Where(char.IsLetter).ToArray());
        var rowStr = new string(cellRef.Where(char.IsDigit).ToArray());

        int row = int.TryParse(rowStr, out var r) ? r : 1;
        int col = ColLettersToIndex(colStr);

        return (row, col);
    }
    /// <summary>Costruisce l'indirizzo di cella (es: "B5") da (row 1-based, col 1-based).</summary>
    public static string BuildCellReference(int row, int col)
        => $"{ColIndexToLetters(col)}{row}";
    /// <summary>Converte lettere colonna Excel (es: "AB") in indice 1-based.</summary>
    public static int ColLettersToIndex(string letters)
    {
        int result = 0;
        foreach (char c in letters.ToUpperInvariant())
            result = result * 26 + (c - 'A' + 1);
        return result;
    }
    /// <summary>Converte indice colonna 1-based in lettere Excel (es: 28 → "AB").</summary>
    public static string ColIndexToLetters(int col)
    {
        var result = string.Empty;
        while (col > 0)
        {
            col--;
            result = (char)('A' + col % 26) + result;
            col /= 26;
        }
        return result;
    }
    /// <summary>
    /// Determina se un dato stile cella rappresenta un formato data/ora.
    /// Controlla sia i format ID standard di Excel (14-22, 45-47) sia i formati custom.
    /// </summary>
    public static bool IsDateFormat(WorkbookPart wbPart, int styleIndex)
    {
        try
        {
            var stylesheet = wbPart.WorkbookStylesPart?.Stylesheet;
            if (stylesheet == null) return false;

            var cellXfs = stylesheet.CellFormats;
            if (cellXfs == null) return false;

            var xf = cellXfs.Elements<CellFormat>().ElementAtOrDefault(styleIndex);
            if (xf == null) return false;

            var numFmtId = (int)(xf.NumberFormatId?.Value ?? 0);

            // Formati data/ora built-in di Excel
            if ((numFmtId >= 14 && numFmtId <= 22) || (numFmtId >= 45 && numFmtId <= 47))
                return true;

            // Controlla formati custom
            if (numFmtId >= 164)
            {
                var numFmt = stylesheet.NumberingFormats?
                    .Elements<NumberingFormat>()
                    .FirstOrDefault(n => n.NumberFormatId?.Value == (uint)numFmtId);

                var fmt = numFmt?.FormatCode?.Value ?? "";
                return fmt.Contains('y') || fmt.Contains('d') || fmt.Contains('h');
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
    /// <summary>Normalizza un riferimento cella rimuovendo i '$' e portandolo in uppercase.</summary>
    public static string NormalizeCellRef(string r) => r.Replace("$", "").ToUpperInvariant();
    public static Dictionary<string, string> ReadHeaders(Row row, SharedStringTable? shared)
        => row.Elements<Cell>()
              .ToDictionary(
                  c => GetColumn(c.CellReference!),
                  c => GetValue(c, shared));
    public static string GetValue(Cell cell, SharedStringTable? shared)
    {
        if (cell.CellValue == null) return string.Empty;
        var v = cell.CellValue.InnerText;
        return cell.DataType?.Value == CellValues.SharedString
            ? shared?.ElementAt(int.Parse(v)).InnerText ?? string.Empty
            : v;
    }
    public static string? ReadSingleValue(SpreadsheetDocument doc, string namedRange)
    {
        // Trova il defined name
        var definedName = doc.WorkbookPart.Workbook
            .DefinedNames?
            .Elements<DefinedName>()
            .FirstOrDefault(d => d.Name == namedRange);

        if (definedName == null)
            throw new Exception($"NamedRange '{namedRange}' non trovato.");

        // Esempio ref: "Report_01!$B$7"
        var def = definedName.Text;
        var parts = def.Split('!');

        var sheetName = parts[0].Trim('\'');      // Rimuove eventuali apici
        var cellRef = parts[1];

        // Trova il foglio
        var sheet = doc.WorkbookPart.Workbook.Descendants<Sheet>()
            .FirstOrDefault(s => s.Name == sheetName);

        if (sheet == null)
            throw new Exception($"Sheet '{sheetName}' non trovato.");

        var wsPart = (WorksheetPart)doc.WorkbookPart.GetPartById(sheet.Id);
        var ws = wsPart.Worksheet;

        // Cerca la cella specifica
        var cell = ws.Descendants<Cell>()
                     .FirstOrDefault(c => c.CellReference == cellRef);

        if (cell == null)
            return null;

        return ReadCellValue(doc, cell);
    }
    public static string? ReadCellValue(SpreadsheetDocument doc, Cell cell)
    {
        if (cell == null)
            return null;

        var value = cell.CellValue?.InnerText;

        // Caso: stringa in SharedStringTable
        if (cell.DataType != null && cell.DataType == CellValues.SharedString)
        {
            var sst = doc.WorkbookPart.SharedStringTablePart.SharedStringTable;
            return sst.ChildElements[int.Parse(value)].InnerText;
        }

        return value;
    }
    public static string GetColumn(string reference)
        => new([.. reference.Where(char.IsLetter)]);
    public static RangeData ReadRangeValues(SpreadsheetDocument doc, string text)
    {
        throw new NotImplementedException();
    }
}