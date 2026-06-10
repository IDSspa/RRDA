using DocumentFormat.OpenXml.Packaging;
using RRDA.Core;
using RRDA.Core.Validator;

namespace RRDA.Plugins.Common
{
    public abstract class BaseImporter : IReportImporter
    {
        // ------------------------------------------------------------------ //
        //  Proprietà identitarie — override obbligatorio nel plugin concreto  //
        // ------------------------------------------------------------------ //
        public abstract string Name { get; }
        public abstract string Version { get; }
        public abstract string SupportedFileExtension { get; }
        public abstract string MatchingPattern { get; }
        public abstract ReportSubjectKind SubjectKind { get; }
        public abstract string SubjectKeyDefinedName { get; }
        /// <summary>
        /// Check if fileName can be imported by the implemented plugin class
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="validationConfigXml"></param>
        /// 
        /// <returns>True if file can be imported false otherwise</returns>
        public virtual Task<bool> CanImportAsync(string fileName, Stream? validationConfigXml = null)
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
        /// <summary>
        /// Import data from report in fileStream, selecting values specified in validationConfigXml.
        /// Trace of the on-going import are notified trough progress and ct parameters.
        /// </summary>
        /// <param name="fileStream"></param>
        /// <param name="config"></param>
        /// <param name="progress"></param>
        /// <param name="ct"></param>
        /// <returns>Import result</returns>
        public async Task<ImportResult> ImportAsync(
                Stream fileStream,
                ValidationConfig config,
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
                ValidateSubjectKeyConfiguration(config);

                // Importa i dati dal file Excel
                var entities = (await ImportDataAsync(fileStream, config, progress, ct)).ToList();
                ValidateImportedEntities(entities);

                result.Entities = entities;
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Errore durante l'importazione: {ex.Message}");
            }

            return result;
        }

        private void ValidateSubjectKeyConfiguration(ValidationConfig config)
        {
            if (string.IsNullOrWhiteSpace(SubjectKeyDefinedName))
            {
                throw new InvalidDataException(
                    $"Il plugin '{Name}' non dichiara un DefinedName valido per la SubjectKey.");
            }

            if (!string.Equals(
                    config.SubjectKeyField,
                    SubjectKeyDefinedName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Il validatore dichiara SubjectKey '{config.SubjectKeyField}', "
                    + $"ma il plugin '{Name}' richiede il DefinedName '{SubjectKeyDefinedName}'.");
            }
        }

        /// <summary>
        /// Legge le celle (singole o range) del file Excel riferite dai DefinedNames
        /// presenti nella configurazione e restituisce le entità da persistere.
        /// </summary>
        /// <param name="xlsxStream">Stream del file .xlsx.</param>
        /// <param name="config">Configurazione di validazione già caricata.</param>
        /// <returns>
        /// Sequenza di <see cref="ReportEntityDto"/> pronti per essere salvati tramite
        /// <see cref="IImportResultRepository.SaveAsync"/>.
        /// Ogni <see cref="ReportEntityDto"/> rappresenta una singola cella (o cella
        /// appartenente ad un range) estratta dal foglio Excel.
        /// </returns>
        /// <remarks>
        /// I plugin con formati non rappresentabili tramite Defined Names possono
        /// sovrascrivere questo metodo. <see cref="ImportAsync"/> mantiene la gestione
        /// comune del risultato, degli errori e della validazione minima dell'output.
        /// </remarks>
        protected virtual Task<IEnumerable<ReportEntityDto>> ImportDataAsync(
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

        private static void ValidateImportedEntities(IReadOnlyList<ReportEntityDto> entities)
        {
            for (var index = 0; index < entities.Count; index++)
            {
                var entity = entities[index]
                    ?? throw new InvalidDataException($"L'entità importata in posizione {index} è null.");

                if (string.IsNullOrWhiteSpace(entity.EntityKind))
                    throw new InvalidDataException($"L'entità importata in posizione {index} non specifica EntityKind.");

                if (string.IsNullOrWhiteSpace(entity.Key))
                    throw new InvalidDataException($"L'entità importata in posizione {index} non specifica Key.");

                if (entity.Properties is null)
                    throw new InvalidDataException($"L'entità importata '{entity.Key}' non specifica Properties.");

                if (!entity.Properties.ContainsKey("value"))
                {
                    throw new InvalidDataException(
                        $"L'entità importata '{entity.Key}' non contiene la proprietà obbligatoria 'value'.");
                }
            }
        }

        public static string SerializeDataType(FieldDataType dt) => dt switch
        {
            FieldDataType.Int => "int",
            FieldDataType.Double => "double",
            FieldDataType.DateTime => "datetime",
            FieldDataType.Bool => "bool",
            _ => "string"
        };
    }
}
