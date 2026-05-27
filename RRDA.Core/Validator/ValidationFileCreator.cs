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
        /// Prefissi riservati da Excel: qualsiasi DefinedName che inizia con uno di
        /// questi valori viene scartato perché non ha significato nel dominio RRDA.
        /// Il prefisso canonico è "_xlnm." (Print_Area, Print_Titles,
        /// Filter_Database, _FilterDatabase, Criteria, Extract, Database, ecc.).
        /// </summary>
        private static readonly string[] ExcludedPrefixes =
        [
            "_xlnm.",   // nomi riservati Excel moderni  (es. _xlnm.Print_Area)
            "__xlnm",   // variante con doppio underscore usata da alcuni provider (es. __xlnm.Print_Area)
            "_xl.",     // variante abbreviata usata da alcuni provider
            "__xl.",    // variante abbreviata con doppio underscore
        ];
        /// <summary>
        /// Nomi esatti da escludere che non seguono un prefisso noto ma restano
        /// non rilevanti per l'applicazione. Case-insensitive.
        /// Estendere questa lista se si individuano altri nomi tecnici da scartare.
        /// </summary>
        private static readonly HashSet<string> ExcludedNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // nomi legacy usati da Excel 97-2003 senza prefisso _xlnm
                "Print_Area",
                "Print_Titles",
                "Filter_Database",
                "_FilterDatabase",
            };

        /// <summary>
        /// Definisce i nomi di campo che, se presenti in un DefinedName, suggeriscono che
        /// il campo è una chiave soggetto.
        /// </summary>
        private static readonly HashSet<string> SubjectKeyNames =             
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Serial",
                "SN"
            };

        /// <summary>
        /// Crea il file di validazione su disco.
        /// </summary>
        /// <param name="xlsxPath">Percorso file .xlsx di input.</param>
        /// <param name="outputXmlPath">Percorso file XML di output (sovrascritto).</param>
        /// <param name="failOnError">Valore dell'attributo failOnError nel root (default: true).</param>
        /// <param name="culture">Valore dell'attributo culture (default: CultureInfo.CurrentCulture.DefinedName).</param>
        public static void CreateFromFile(string xlsxPath, string outputXmlPath, bool failOnError = true, string? culture = null)
        {
            if (string.IsNullOrWhiteSpace(xlsxPath)) throw new ArgumentNullException(nameof(xlsxPath));
            if (string.IsNullOrWhiteSpace(outputXmlPath)) throw new ArgumentNullException(nameof(outputXmlPath));

            using var inFs = File.OpenRead(xlsxPath);
            using var outFs = File.Create(outputXmlPath);
            CreateFromStream(inFs, outFs, failOnError, culture);
        }
        /// <summary>
        /// Crea il file di validazione scrivendo l'XML su uno stream di output.
        /// Lo stream di input può essere non seekable (viene gestito internamente).
        /// </summary>
        /// <param name="xlsxStream">Stream di input .xlsx (non chiuso dalla routine).</param>
        /// <param name="outputXmlStream">Stream di output per l'XML (non chiuso dalla routine).</param>
        /// <param name="failOnError">Valore dell'attributo failOnError nel root (default: true).</param>
        /// <param name="culture">Valore dell'attributo culture (default: CultureInfo.CurrentCulture.DefinedName).</param>
        public static void CreateFromStream(Stream xlsxStream, Stream outputXmlStream, bool failOnError = true, string? culture = null)
        {
            ArgumentNullException.ThrowIfNull(xlsxStream);
            ArgumentNullException.ThrowIfNull(outputXmlStream);

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

                var definedNames = allDefinedNames
                    .Where(dn => IsRelevantDefinedName(dn.Name?.Value))
                    .ToList();

                var sheets = wb?.Sheets?.Elements<Sheet>()?.ToList() ?? [];

                var subjectKey = definedNames.FirstOrDefault(dn => dn.Name?.Value is string name && SubjectKeyNames.Contains(name))?.Name?.Value;

                // root conforme a ValidationConfig.xsd
                var root = new XElement("ValidationConfig");
                root.SetAttributeValue("failOnError", failOnError.ToString().ToLowerInvariant());
                root.SetAttributeValue("culture", (culture ?? CultureInfo.CurrentCulture.Name));

                // Imposta il valore di subjectKeyField se è stato identificato un DefinedName che corrisponde a una chiave soggetto
                if (!string.IsNullOrEmpty(subjectKey))
                    root.SetAttributeValue("subjectKeyField", subjectKey);

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
                        var dnName = dn.Name ?? string.Empty;
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
                    var dnName = dn.Name ?? string.Empty;
                    var inferredType = InferTypeForDefinedName(dn, sheetIndex,
                                       wbPart.SharedStringTablePart?.SharedStringTable,
                                       wbPart);
                    var fieldEl = new XElement("Field",
                        new XAttribute("definedName", dnName),
                        new XAttribute("required", "false"),
                        new XAttribute("type", inferredType)
                    );
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
        /// <summary>
        /// Restituisce <c>false</c> per i DefinedNames riservati da Excel o comunque
        /// non rilevanti per il dominio RRDA; <c>true</c> per tutti gli altri.
        /// </summary>
        private static bool IsRelevantDefinedName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            // Esclusione per prefisso (es. _xlnm.Print_Area, _xl.SomeInternalName)
            foreach (var prefix in ExcludedPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // Esclusione per nome esatto (nomi legacy senza prefisso noto)
            if (ExcludedNames.Contains(name))
                return false;

            return true;
        }
        private static string InferTypeForDefinedName(DefinedName dn, Dictionary<string, WorksheetPart> sheetIndex, SharedStringTable? sharedStrings, WorkbookPart wbPart)
        {
            var cell = ResolveCellForDefinedName(dn, sheetIndex);
            if (cell == null) return "string";

            // 1. Bool
            if (cell.DataType?.Value == CellValues.Boolean)
                return "bool";

            var raw = cell.CellValue?.InnerText ?? string.Empty;

            // 2. DateTime (stile cella)
            var styleIdx = (int)(cell.StyleIndex?.Value ?? 0);
            if (IsDateStyleIndex(wbPart, styleIdx)
                && double.TryParse(raw, NumberStyles.Any,
                                   CultureInfo.InvariantCulture, out _))
                return "datetime";

            // SharedString → testo puro, non può essere numerico
            if (cell.DataType?.Value == CellValues.SharedString)
                return "string";

            // 3. Int
            if (long.TryParse(raw, out _))
                return "int";

            // 4. Double
            if (double.TryParse(raw, NumberStyles.Any,
                                CultureInfo.InvariantCulture, out _))
                return "double";

            // 5. Fallback
            return "string";
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