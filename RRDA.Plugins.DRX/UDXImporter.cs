using DocumentFormat.OpenXml.Packaging;
using RRDA.Core;
using RRDA.Core.Validator;
using RRDA.Plugins.Common;

namespace RRDA.Plugins.DRX
{
    public sealed class DRXImporter : IReportImporter
    {
        public string Name => "DRX";
        public string Version => "1.0.0";
        public string SupportedFileExtension => ".xlsx";
        public string MatchingPattern => "NCH_2022_002_DRX_MAN";
        public string EntityKind => "TestMeasurement";
        public Task<bool> CanImportAsync(string fileName, Stream? fileStream = null, Stream? validationConfigXml = null)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return Task.FromResult(false);

            var actualFileName = Path.GetFileName(fileName);
            if (string.IsNullOrEmpty(actualFileName))
                return Task.FromResult(false);

            // Controlla estensione
            var ext = Path.GetExtension(actualFileName);
            if (!string.Equals(ext, SupportedFileExtension, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(false);

            // Controlla se il nome (senza estensione) contiene il pattern definito (case-insensitive)
            var nameWithoutExt = Path.GetFileNameWithoutExtension(actualFileName) ?? string.Empty;
            var matches = nameWithoutExt.Contains(MatchingPattern, StringComparison.OrdinalIgnoreCase);

            return Task.FromResult(matches);
        }
        public async Task<ImportResult> ImportAsync(
                Stream fileStream,
                Stream validationConfigXml,
                IProgress<ImportProgress>? progress = null,
                CancellationToken ct = default)
        {
            var result = new ImportResult
            {
                ReportTypeKey = Name,
                Success = false
            };

            try
            {
                // 1. Carica e valida la configurazione XML
                var config = ValidationConfig.Load(validationConfigXml);

                // 2. Importa i dati dal file Excel
                var entities = await ImportData(fileStream, config);

                result.Entities = entities;
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Errore durante l'importazione: {ex.Message}");
            }

            return result;
        }
        // -------------------------------------------------------------------------
        // Metodo principale di importazione dati
        // -------------------------------------------------------------------------
        /// <summary>
        /// Legge le celle (singole o range) del file Excel riferite dai DefinedNames
        /// presenti nella configurazione e restituisce le entità da persistere.
        /// </summary>
        /// <param name="xlsxStream">Stream del file .xlsx.</param>
        /// <param name="config">Configurazione di validazione già caricata.</param>
        /// <returns>
        /// Sequenza di <see cref="ReportEntityDto"/> pronti per essere salvati tramite
        /// <see cref="ImportResultRepository.SaveAsync"/>.
        /// Ogni <see cref="ReportEntityDto"/> rappresenta una singola cella (o cella
        /// appartenente ad un range) estratta dal foglio Excel.
        /// </returns>
        private static Task<IEnumerable<ReportEntityDto>> ImportData(
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
                using var doc = SpreadsheetDocument.Open(input, isEditable: false);

                var wbPart = doc.WorkbookPart
                             ?? throw new InvalidOperationException("WorkbookPart non trovato nel file xlsx.");

                var workbook = wbPart.Workbook;

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

                // Iteriamo sui FieldRules definiti nel config
                foreach (var rule in config.FieldRules)
                {
                    int count = 0;

                    ct.ThrowIfCancellationRequested();

                    var definedName = rule.DefinedName;

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

                        entities.Add(new ReportEntityDto
                        {
                            EntityKind = sheetName,
                            Key = definedName,
                            Properties = new Dictionary<string, string>
                            {
                                ["name"] = definedName,
                                ["value"] = cellValue,
                                ["unit"] = rule.Unit ?? string.Empty,
                                //["datatype"] = rule.DataType,
                                ["row_index"] = "-1",
                                ["col_index"] = "-1"
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
                                    Key = $"{definedName}[{dr},{dc}]",
                                    Properties = new Dictionary<string, string>
                                    {
                                        ["name"] = definedName,
                                        ["value"] = cellValue,
                                        ["unit"] = rule.Unit ?? string.Empty,
                                        //["datatype"] = rule.DataType,
                                        ["row_index"] = dr.ToString(),
                                        ["col_index"] = dc.ToString()
                                    }
                                });
                            }
                        }
                    }

                    progress?.Report(new ImportProgress(count++, config.FieldRules.Count, $"Elaborazione riga {count + 1} di {config.FieldRules.Count}…"));
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
