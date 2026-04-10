using RRDA.Core;
using RRDA.Core.Validator;
using RRDA.Plugins.Common;

namespace RRDA.Plugins.Dummy
{
    public class DummyImporter : IReportImporter
    {
        public string Name => "Dummy";
        public string Version => "1.0.0";
        public string SupportedFileExtension => ".xlsx";
        public string MatchingPattern => "Foglio";
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
            var matches = nameWithoutExt.IndexOf(MatchingPattern, StringComparison.OrdinalIgnoreCase) >= 0;

            return Task.FromResult(matches);
        }

        public async Task<ImportResult> ImportAsync(
            Stream fileStream,
            Stream validationConfigXml)
        {
            var cfg = ValidationConfig.Load(validationConfigXml);
            var entities = new List<ReportEntityDto>();
            var errors = new List<string>();

            var _rows = OpenXmlExcelReader.ReadRows(fileStream, cfg.Sheets);

            foreach (var row in _rows)
            {
                // Salta le righe non significative (tutte le celle vuote o solo whitespace)
                if (row == null || row.Values.Any(v => string.IsNullOrWhiteSpace(v)))
                    continue;

                try
                {
                    var dto = ExcelRowMapper.Map(
                        row,
                        cfg,
                        entityKind: EntityKind,
                        keySelector: r =>
                            r.TryGetValue("Serial", out var s) ? s ?? string.Empty : string.Empty);

                    entities.Add(dto);
                }
                catch (Exception ex)
                {
                    errors.Add(ex.Message);
                }
            }

            return new ImportResult
            {
                ReportTypeKey = Name,
                Entities = entities,
                Errors = errors,
                Success = errors.Count == 0
            };
        }

    }
}
