using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Globalization;
using System.Xml.Linq;

namespace RRDA.Core.Validator
{
    /// <summary>
    /// Crea un file di validazione XML conforme a RRDA.Core\Validator\ValidationConfig.xsd
    /// a partire dai DefinedNames presenti in un .xlsx.
    /// </summary>
    public static class ValidationFileCreator
    {
        /// <summary>
        /// Crea il file di validazione su disco.
        /// </summary>
        /// <param name="xlsxPath">Percorso file .xlsx di input.</param>
        /// <param name="outputXmlPath">Percorso file XML di output (sovrascritto).</param>
        /// <param name="subjectKeyDefinedName">DefinedName SubjectKey dichiarato dal plugin.</param>
        /// <param name="unitMappingsPath">Percorso opzionale del file XML con i mapping delle unità di misura.</param>
        /// <param name="failOnError">Valore dell'attributo failOnError nel root (default: true).</param>
        /// <param name="culture">Valore dell'attributo culture (default: CultureInfo.CurrentCulture.DefinedName).</param>
        /// <param name="importBanListPath">Percorso opzionale del file XML con le esclusioni di importazione.</param>
        public static void CreateFromFile(
            string xlsxPath,
            string outputXmlPath,
            string subjectKeyDefinedName,
            string? unitMappingsPath = null,
            bool failOnError = true,
            string? culture = null,
            string? importBanListPath = null)
        {
            if (string.IsNullOrWhiteSpace(xlsxPath)) throw new ArgumentNullException(nameof(xlsxPath));
            if (string.IsNullOrWhiteSpace(outputXmlPath)) throw new ArgumentNullException(nameof(outputXmlPath));

            using var inFs = File.OpenRead(xlsxPath);
            using var outFs = File.Create(outputXmlPath);
            CreateFromStream(inFs, outFs, subjectKeyDefinedName, unitMappingsPath, failOnError, culture, importBanListPath);
        }
        /// <summary>
        /// Crea il file di validazione scrivendo l'XML su uno stream di output.
        /// Lo stream di input può essere non seekable (viene gestito internamente).
        /// </summary>
        /// <param name="xlsxStream">Stream di input .xlsx (non chiuso dalla routine).</param>
        /// <param name="outputXmlStream">Stream di output per l'XML (non chiuso dalla routine).</param>
        /// <param name="subjectKeyDefinedName">DefinedName SubjectKey dichiarato dal plugin.</param>
        /// <param name="unitMappingsPath">Percorso opzionale del file XML con i mapping delle unità di misura.</param>
        /// <param name="failOnError">Valore dell'attributo failOnError nel root (default: true).</param>
        /// <param name="culture">Valore dell'attributo culture (default: CultureInfo.CurrentCulture.DefinedName).</param>
        /// <param name="importBanListPath">Percorso opzionale del file XML con le esclusioni di importazione.</param>
        public static void CreateFromStream(
            Stream xlsxStream,
            Stream outputXmlStream,
            string subjectKeyDefinedName,
            string? unitMappingsPath = null,
            bool failOnError = true,
            string? culture = null,
            string? importBanListPath = null)
        {
            ArgumentNullException.ThrowIfNull(xlsxStream);
            ArgumentNullException.ThrowIfNull(outputXmlStream);
            ArgumentException.ThrowIfNullOrWhiteSpace(subjectKeyDefinedName);
            var unitMappings = UnitMappingResolver.Load(unitMappingsPath);
            var importBanList = ImportBanListResolver.Load(importBanListPath);

            // SpreadsheetDocument richiede uno stream seekable: copiamo se necessario
            Stream input = xlsxStream;
            MemoryStream? buffer = null;
            if (!xlsxStream.CanSeek)
            {
                buffer = new MemoryStream();
                xlsxStream.CopyTo(buffer);
                buffer.Position = 0;
                input = buffer;
            }

            try
            {
                using var doc = SpreadsheetDocument.Open(input, false);
                var wb = doc.WorkbookPart?.Workbook;
                var wbPart = doc.WorkbookPart ?? throw new InvalidOperationException("WorkbookPart non trovato nel file xlsx.");

                // Tutti i DefinedNames del workbook, filtrati da quelli irrilevanti
                var allDefinedNames = wb?.DefinedNames?.Elements<DefinedName>().ToList()
                       ?? [];

                var sheets = wb?.Sheets?.Elements<Sheet>()
                    .Where(sheet => !importBanList.IsSheetExcluded(sheet.Name?.Value))
                    .ToList() ?? [];

                var definedNames = allDefinedNames
                    .Where(dn => !string.IsNullOrWhiteSpace(dn.Name?.Value))
                    .Where(dn => !importBanList.IsDefinedNameExcluded(dn.Name?.Value))
                    .Where(dn => !importBanList.IsSheetExcluded(GetSheetName(dn)))
                    .ToList();

                var subjectKeyExists = definedNames.Any(dn => string.Equals(
                    dn.Name?.Value,
                    subjectKeyDefinedName,
                    StringComparison.OrdinalIgnoreCase));
                if (!subjectKeyExists)
                {
                    var subjectKey = allDefinedNames.FirstOrDefault(dn => string.Equals(
                        dn.Name?.Value,
                        subjectKeyDefinedName,
                        StringComparison.OrdinalIgnoreCase));
                    if (subjectKey is not null
                        && (importBanList.IsDefinedNameExcluded(subjectKeyDefinedName)
                            || importBanList.IsSheetExcluded(GetSheetName(subjectKey))))
                    {
                        throw new InvalidDataException(
                            $"Il DefinedName SubjectKey '{subjectKeyDefinedName}' è escluso dalla banlist di importazione.");
                    }

                    throw new InvalidDataException(
                        $"Il DefinedName SubjectKey '{subjectKeyDefinedName}' dichiarato dal plugin non è presente nel workbook.");
                }

                // root conforme a ValidationConfig.xsd
                var root = new XElement("ValidationConfig");
                root.SetAttributeValue("failOnError", failOnError.ToString().ToLowerInvariant());
                root.SetAttributeValue("culture", (culture ?? CultureInfo.CurrentCulture.Name));

                root.SetAttributeValue("subjectKeyField", subjectKeyDefinedName);

                // Indice sheetName → WorksheetPart (riuso stesso pattern di OpenXmlExcelReader)
                var sheetIndex = BuildSheetIndex(wb!, wbPart);

                // Sheets (minOccurs=0) -> aggiungiamo se ci sono sheets
                if (sheets.Count > 0)
                {
                    var _sheets = new XElement("Sheets");

                    foreach (var sheet in sheets)
                    {
                        var _sheet = new XElement("Sheet",
                            new XAttribute("Name", sheet.Name ?? string.Empty)
                        );
                        _sheets.Add(_sheet);
                    }
                    root.Add(_sheets);
                }

                // FieldMappings (minOccurs=0) -> aggiungiamo se ci sono defined names
                if (definedNames.Count > 0)
                {
                    var fieldMappings = new XElement("FieldMappings");
                    foreach (var dn in definedNames)
                    {
                        var dnName = dn.Name?.Value ?? string.Empty;
                        var map = new XElement("Map",
                            new XAttribute("definedName", dnName),
                            new XAttribute("field", dnName)
                        );
                        fieldMappings.Add(map);
                    }
                    root.Add(fieldMappings);
                }

                // Fields (obbligatorio secondo XSD) -> creiamo almeno un'entry per ogni DefinedName;
                // impostiamo type string e required false come default.
                // TODO: verificare se definedNames.Count == 0 non possiamo creare nessuna FieldRule -> eccezione,
                // possibile dedurre il formato del campo dal suo valore (ex. tryParse() in cascata)
                var fieldsEl = new XElement("FieldRules");
                foreach (var dn in definedNames)
                {
                    var dnName = dn.Name?.Value ?? string.Empty;
                    var inferredType = InferTypeForDefinedName(dn, sheetIndex,
                                       wbPart.SharedStringTablePart?.SharedStringTable,
                                       wbPart);
                    var fieldEl = new XElement("Field",
                        new XAttribute("definedName", dnName),
                        new XAttribute("required", "false"),
                        new XAttribute("type", inferredType)
                    );
                    if (IsNumericType(inferredType))
                    {
                        var inferredUnit = unitMappings.InferUnit(dnName);
                        if (inferredUnit is not null)
                            fieldEl.SetAttributeValue("unit", inferredUnit);
                    }

                    fieldsEl.Add(fieldEl);
                }
                // anche se non ci sono defined names, Fields deve esistere (XSD non lo rende opzionale)
                root.Add(fieldsEl);

                // RowRules (minOccurs=0) -> non popoliamo regole, ma l'elemento è opzionale.
                // Se si desidera, si può aggiungere un placeholder vuoto; lasciamo assente per pulizia.
                // root.Add(new XElement("RowRules"));

                // CustomValidators (minOccurs=0) -> non aggiungiamo nulla qui.

                var docXml = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
                docXml.Save(outputXmlStream);
                outputXmlStream.Flush();
            }
            finally
            {
                buffer?.Dispose();
            }
        }
        private static string InferTypeForDefinedName(DefinedName dn, Dictionary<string, WorksheetPart> sheetIndex, SharedStringTable? sharedStrings, WorkbookPart wbPart)
        {
            var cell = ResolveCellForDefinedName(dn, sheetIndex);
            if (cell == null) return "string";

            // 1. Bool
            if (cell.DataType?.Value == CellValues.Boolean)
                return "bool";

            var raw = ReadCellText(cell, sharedStrings);

            // 2. DateTime (stile cella)
            var styleIdx = (int)(cell.StyleIndex?.Value ?? 0);
            if (IsDateStyleIndex(wbPart, styleIdx)
                && double.TryParse(raw, NumberStyles.Any,
                                   CultureInfo.InvariantCulture, out _))
                return "datetime";

            // 3. Int
            if (long.TryParse(raw, NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out _))
                return "int";

            // 4. Double
            if (double.TryParse(raw, NumberStyles.Any,
                                CultureInfo.InvariantCulture, out _))
                return "double";

            // 5. Fallback
            return "string";
        }
        private static bool IsNumericType(string inferredType)
            => inferredType is "int" or "double";

        private static string ReadCellText(Cell cell, SharedStringTable? sharedStrings)
        {
            if (cell.DataType?.Value == CellValues.SharedString
                && int.TryParse(
                    cell.CellValue?.InnerText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var sharedStringIndex))
            {
                return sharedStrings?
                    .Elements<SharedStringItem>()
                    .ElementAtOrDefault(sharedStringIndex)?
                    .InnerText ?? string.Empty;
            }

            if (cell.DataType?.Value == CellValues.InlineString)
                return cell.InlineString?.InnerText ?? string.Empty;

            return cell.CellValue?.InnerText ?? string.Empty;
        }
        private static Cell? ResolveCellForDefinedName(DefinedName dn, Dictionary<string, WorksheetPart> sheetIndex)
        {
            var formula = dn.Text;
            if (string.IsNullOrWhiteSpace(formula)) return null;

            // Prende solo la prima cella del range (prima del ':')
            var firstPart = formula.Split(':')[0].Trim();
            var bangIdx = firstPart.LastIndexOf('!');
            if (bangIdx < 0) return null;

            var sheetName = firstPart[..bangIdx].Trim('\'');
            var cellAddr = firstPart[(bangIdx + 1)..].Replace("$", "")
                                              .ToUpperInvariant();

            if (!sheetIndex.TryGetValue(sheetName, out var wsPart) || wsPart.Worksheet == null)
                return null;

            return wsPart.Worksheet
                         .Descendants<Cell>()
                         .FirstOrDefault(c =>
                             NormalizeCellRef(c.CellReference?.Value ?? "") == cellAddr);
        }
        private static string? GetSheetName(DefinedName dn)
        {
            var formula = dn.Text;
            if (string.IsNullOrWhiteSpace(formula)) return null;

            var firstPart = formula.Split(':')[0].Trim();
            var bangIdx = firstPart.LastIndexOf('!');
            if (bangIdx < 0) return null;

            return firstPart[..bangIdx].Trim('\'');
        }
        private static Dictionary<string, WorksheetPart> BuildSheetIndex(Workbook workbook, WorkbookPart wbPart)
        {
            var index = new Dictionary<string, WorksheetPart>(StringComparer.OrdinalIgnoreCase);
            foreach (Sheet sheet in workbook.Sheets?.Elements<Sheet>() ?? [])
            {
                if (sheet.Name is null || sheet.Id?.Value is null) continue;
                if (wbPart.GetPartById(sheet.Id.Value) is WorksheetPart wsPart)
                    index[sheet.Name.Value!] = wsPart;
            }
            return index;
        }
        private static bool IsDateStyleIndex(WorkbookPart wbPart, int styleIndex)
        {
            try
            {
                var xf = wbPart.WorkbookStylesPart?.Stylesheet
                               ?.CellFormats?.Elements<CellFormat>()
                               .ElementAtOrDefault(styleIndex);
                if (xf == null) return false;

                var numFmtId = (int)(xf.NumberFormatId?.Value ?? 0);
                if ((numFmtId >= 14 && numFmtId <= 22) || (numFmtId >= 45 && numFmtId <= 47))
                    return true;

                if (numFmtId >= 164)
                {
                    var fmt = wbPart.WorkbookStylesPart?.Stylesheet?.NumberingFormats?
                                   .Elements<NumberingFormat>()
                                   .FirstOrDefault(n => n.NumberFormatId?.Value == (uint)numFmtId)
                                   ?.FormatCode?.Value ?? "";
                    return fmt.Contains('y') || fmt.Contains('d') || fmt.Contains('h');
                }
                return false;
            }
            catch { return false; }
        }
        private static string NormalizeCellRef(string r)
            => r.Replace("$", "").ToUpperInvariant();
    }
}
