using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using RRDA.Core;
using RRDA.Core.Validator;
using RRDA.Plugins.Common;
using System.Text.RegularExpressions;


namespace RRDA.Plugins.IBASP
{
    public sealed class IBASPImporter : BaseImporter
    {
        public override string Name => "IBASP";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override string MatchingPattern => "IBASP";
        public override ReportSubjectKind SubjectKind => ReportSubjectKind.Component;
        public override string SubjectKeyDefinedName => "Serial";       
        private static void Sanitize(Stream xlsxStream)
        {
            ArgumentNullException.ThrowIfNull(xlsxStream);

            // Assicura che lo stream sia posizionato all'inizio
            if (xlsxStream.CanSeek)
                xlsxStream.Position = 0;

            using (var document = SpreadsheetDocument.Open(xlsxStream, true))
            {
                var workbookPart = document.WorkbookPart;
                if (workbookPart?.Workbook?.DefinedNames == null)
                    return;

                var definedNames = workbookPart.Workbook.DefinedNames.Elements<DefinedName>().ToList();

                // --- PASSAGGIO 1 ---
                // Sostituisce ETH1 -> ETHA e ETH2 -> ETHB
                foreach (var dn in definedNames)
                {
                    if (dn.Name != null)
                    {
                        var name = dn.Name.Value;

                        if (name.Contains("ETH1"))
                            name = name.Replace("ETH1", "ETHA");

                        if (name.Contains("ETH2"))
                            name = name.Replace("ETH2", "ETHB");

                        dn.Name = name;
                    }
                }

                // --- PASSAGGIO 2 ---
                // Rimuove numeri finali (es. Name123 -> Name)
                var regexTrailingDigits = new Regex(@"\d+$");

                foreach (var dn in definedNames)
                {
                    if (dn.Name != null)
                    {
                        var name = dn.Name.Value;
                        var cleaned = regexTrailingDigits.Replace(name, "");
                        dn.Name = cleaned;
                    }
                }

                // --- PASSAGGIO 3 ---
                // Ripristino ETHA -> ETH1 e ETHB -> ETH2
                foreach (var dn in definedNames)
                {
                    if (dn.Name != null)
                    {
                        var name = dn.Name.Value;

                        if (name.Contains("ETHA"))
                            name = name.Replace("ETHA", "ETH1");

                        if (name.Contains("ETHB"))
                            name = name.Replace("ETHB", "ETH2");

                        dn.Name = name;
                    }
                }

                // Salva le modifiche
                workbookPart.Workbook.Save();
            }

            // Reset dello stream per utilizzi successivi
            if (xlsxStream.CanSeek)
                xlsxStream.Position = 0;
        }
        public Task<IEnumerable<ReportEntityDto>> ImportDataAsync(
                Stream xlsxStream,
                ValidationConfig config,
                IProgress<ImportProgress>? progress = null,
                CancellationToken ct = default)
        {
            // SpreadsheetDocument richiede uno stream seekable
            Stream input = xlsxStream;

            MemoryStream? buffer = null;

            if (!xlsxStream.CanSeek)
            {
                buffer = new MemoryStream();
                xlsxStream.CopyTo(buffer);
                buffer.Position = 0;
                input = buffer;
            }

            var entities = new List<ReportEntityDto>();

            try
            {
                Sanitize(input);

                using var doc = SpreadsheetDocument.Open(input, isEditable: false);

                var wbPart = doc.WorkbookPart
                             ?? throw new InvalidOperationException("WorkbookPart non trovato nel file xlsx.");

                var workbook = wbPart.Workbook
                               ?? throw new InvalidDataException("Workbook non trovato nel file xlsx.");

                // Shared strings (opzionale – non tutti i file ce l'hanno)
                var sharedStrings = wbPart.SharedStringTablePart?.SharedStringTable;

                // Costruiamo un indice rapido  sheetName -> WorksheetPart
                var sheetIndex = OpenXmlExcelReader.BuildSheetIndex(workbook, wbPart);

                // Costruiamo un indice  definedName -> (sheetName, cellRef) dei DefinedNames della workbook
                var definedNamesIndex = OpenXmlExcelReader.BuildDefinedNamesIndex(workbook);

                // Set dei worksheet ammessi (case-insensitive) — da sezione <Sheets> del config
                var allowedSheets = new HashSet<string>(
                    config.Sheets
                          .Select(s => s.Name?.Value ?? string.Empty)
                          .Where(n => !string.IsNullOrWhiteSpace(n)),
                    StringComparer.OrdinalIgnoreCase);

                var aliasesByDefinedName = config.Mappings
                    .Where(m => !string.IsNullOrWhiteSpace(m.DefinedName))
                    .GroupBy(m => m.DefinedName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(m => m.Alias)
                              .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a))
                              ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase);

                int index = 0;

                // Iteriamo sui FieldRules definiti nel config
                foreach (var rule in config.FieldRules)
                {
                    ct.ThrowIfCancellationRequested();

                    progress?.Report(
                        new ImportProgress(
                                index,
                                config.FieldRules.Count,
                                $"Elaborazione riga {index + 1} di {config.FieldRules.Count}…"
                        )
                    );

                    var definedName = rule.DefinedName;
                    var fieldName = aliasesByDefinedName.TryGetValue(definedName, out var alias)
                        && !string.IsNullOrWhiteSpace(alias)
                        ? alias
                        : definedName;

                    if (string.IsNullOrWhiteSpace(definedName))
                        continue;

                    // Risolviamo il DefinedName nella coppia (sheetName, cellAddress)
                    if (!definedNamesIndex.TryGetValue(definedName, out var cellRef))
                        continue; // DefinedName non presente nel file xlsx – saltiamo

                    var (sheetName, startAddress) = cellRef;

                    // Verifichiamo che il foglio sia tra quelli permessi
                    if (!allowedSheets.Contains(sheetName))
                        continue;

                    // Recuperiamo il WorksheetPart relativo al foglio
                    if (!sheetIndex.TryGetValue(sheetName, out var wsPart))
                        continue;

                    // Calcoliamo le coordinate (riga, colonna) della cella di partenza
                    var (startRow, startCol) = OpenXmlExcelReader.ParseCellReference(startAddress);

                    if (rule.Range is null)
                    {
                        // -----------------------------------------------------------------
                        // SINGOLA CELLA  →  row_index = -1, col_index = -1
                        // -----------------------------------------------------------------
                        var cellValue = OpenXmlExcelReader.ReadCellValue(wsPart, startAddress, sharedStrings, wbPart);
                        var isSubjectKey = string.Equals(
                            definedName,
                            SubjectKeyDefinedName,
                            StringComparison.OrdinalIgnoreCase);

                        entities.Add(new ReportEntityDto
                        {
                            EntityKind = sheetName,
                            Key = fieldName,
                            Properties = new Dictionary<string, string>
                            {
                                ["name"] = fieldName,
                                ["value"] = cellValue,
                                ["unit"] = rule.Unit ?? string.Empty,
                                ["row_index"] = "-1",
                                ["col_index"] = "-1",
                                ["is_subject_key"] = isSubjectKey ? "1" : "0",
                                ["data_type"] = SerializeDataType(rule.DataType)
                            }
                        });
                    }
                    else
                    {
                        // -----------------------------------------------------------------
                        // RANGE DI CELLE  →  row_index e col_index relativi alla cella iniziale
                        // -----------------------------------------------------------------
                        int rowCount = rule.Range.row_count > 0 ? rule.Range.row_count : 1;
                        int colCount = rule.Range.col_count > 0 ? rule.Range.col_count : 1;

                        for (int dr = 0; dr < rowCount; dr++)
                        {
                            for (int dc = 0; dc < colCount; dc++)
                            {
                                int absRow = startRow + dr;
                                int absCol = startCol + dc;

                                var addr = OpenXmlExcelReader.BuildCellReference(absRow, absCol);
                                var cellValue = OpenXmlExcelReader.ReadCellValue(wsPart, addr, sharedStrings, wbPart);

                                entities.Add(new ReportEntityDto
                                {
                                    EntityKind = sheetName,
                                    // Key distingue univocamente range + posizione nel range
                                    Key = $"{fieldName}[{dr},{dc}]",
                                    Properties = new Dictionary<string, string>
                                    {
                                        ["name"] = fieldName,
                                        ["value"] = cellValue,
                                        ["unit"] = rule.Unit ?? string.Empty,
                                        ["row_index"] = dr.ToString(),
                                        ["col_index"] = dc.ToString(),
                                        ["data_type"] = SerializeDataType(rule.DataType)
                                    }
                                });
                            }
                        }
                    }

                    index++;
                }
            }
            finally
            {
                buffer?.Dispose();
            }

            return Task.FromResult<IEnumerable<ReportEntityDto>>(entities);
        }
    }
}
