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
            if (xlsxStream == null) throw new ArgumentNullException(nameof(xlsxStream));
            if (outputXmlStream == null) throw new ArgumentNullException(nameof(outputXmlStream));

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
                var definedNames = wb?.DefinedNames?.Elements<DefinedName>().ToList()
                                   ?? Enumerable.Empty<DefinedName>().ToList();
                var sheets = wb?.Sheets?.Elements<Sheet>()?.ToList() ?? Enumerable.Empty<Sheet>().ToList();

                // root conforme a ValidationConfig.xsd
                var root = new XElement("ValidationConfig");
                root.SetAttributeValue("failOnError", failOnError.ToString().ToLowerInvariant());
                root.SetAttributeValue("culture", (culture ?? CultureInfo.CurrentCulture.Name));

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
                    var fieldEl = new XElement("Field",
                        new XAttribute("definedName", dnName),
                        new XAttribute("required", "false"),
                        new XAttribute("type", "string")
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
    }
}