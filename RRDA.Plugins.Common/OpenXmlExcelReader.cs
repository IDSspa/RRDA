using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace RRDA.Plugins.Common
{
    public static class OpenXmlExcelReader
    {
        /// <summary>
        /// Legge il valore testuale di una singola cella identificata dal suo indirizzo (es: "B5").
        /// Gestisce SharedString, valori numerici, date e testo inline.
        /// </summary>
        public static string ReadCellValue(WorksheetPart wsPart, string cellAddress, SharedStringTable? sharedStrings, WorkbookPart wbPart)
        {
            // Controllo nullo su Worksheet
            if (wsPart.Worksheet == null)
                return string.Empty;

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
                                             ?? [])
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
                               ?? [];

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
            var colStr = new string([.. cellRef.Where(char.IsLetter)]);
            var rowStr = new string([.. cellRef.Where(char.IsDigit)]);

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
                  .Where(c => !string.IsNullOrWhiteSpace(c.CellReference?.Value))
                  .ToDictionary(
                      c => GetColumn(c.CellReference?.Value
                          ?? throw new InvalidDataException("Riferimento cella mancante nell'header.")),
                      c => GetValue(c, shared));
        public static string GetValue(Cell cell, SharedStringTable? shared)
        {
            if (cell.CellValue == null) return string.Empty;
            var v = cell.CellValue.InnerText;
            if (cell.DataType?.Value != CellValues.SharedString)
                return v;

            return int.TryParse(v, out var index)
                ? shared?.Elements<SharedStringItem>().ElementAtOrDefault(index)?.InnerText ?? string.Empty
                : string.Empty;
        }
        public static string? ReadCellValue(SpreadsheetDocument doc, Cell cell)
        {
            var value = cell.CellValue?.InnerText;

            // Caso: stringa in SharedStringTable
            if (cell.DataType?.Value == CellValues.SharedString)
            {
                if (!int.TryParse(value, out var index))
                    return null;

                return doc.WorkbookPart?
                    .SharedStringTablePart?
                    .SharedStringTable?
                    .Elements<SharedStringItem>()
                    .ElementAtOrDefault(index)?
                    .InnerText;
            }

            return value;
        }
        public static string GetColumn(string reference)
            => new([.. reference.Where(char.IsLetter)]);
    }
}
