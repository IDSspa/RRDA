using RRDA.Core;

namespace RRDA.Plugins.SBX
{
    public sealed class SBXImporter : IReportImporter
    {
        public string Name => "SBX";
        public string Version => "1.0.0";
        public string SupportedFileExtension => ".xlsx";
        public string MatchingPattern => "NCH_2022_004_1_3_LXOD-RS_SBX";
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

        public Task<ImportResult> ImportAsync(Stream fileStream, Stream validationConfigXml)
        {
            throw new NotImplementedException();
        }
    }
}
