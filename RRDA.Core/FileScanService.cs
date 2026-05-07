namespace RRDA.Core
{
    public sealed class FileScanService : IFileScanService
    {
        public async Task<IReadOnlyList<ScannedReportFile>> ScanAsync(
            FileScanRequest request,
            IReadOnlyCollection<IReportImporter> importers,
            IProgress<FileScanProgress>? progress = null,
            Action<string>? log = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.RootFolder))
                throw new ArgumentException("Cartella root non valida.", nameof(request));

            if (!Directory.Exists(request.RootFolder))
                return [];

            var maxDepth = Math.Max(0, request.MaxDepth);
            var pattern = string.IsNullOrWhiteSpace(request.SearchPattern)
                ? "*.xlsx"
                : request.SearchPattern;

            progress?.Report(new FileScanProgress(
                FileScanPhase.ScanningDirectories,
                null,
                null,
                request.RootFolder,
                "Scansione cartelle in corso..."));

            var fileInfos = EnumerateFiles(
                request.RootFolder,
                pattern,
                maxDepth,
                progress,
                log,
                cancellationToken)
                .OrderBy(f => f.Name)
                .ToList();

            progress?.Report(new FileScanProgress(
                FileScanPhase.ClassifyingFiles,
                0,
                fileInfos.Count,
                null,
                "Catalogazione file in corso..."));

            var result = new List<ScannedReportFile>(fileInfos.Count);

            for (var i = 0; i < fileInfos.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var file = fileInfos[i];
                var matchedPlugin = await FindMatchingPluginAsync(
                    file,
                    importers,
                    log,
                    cancellationToken);

                result.Add(new ScannedReportFile(
                    file.Name,
                    file.FullName,
                    file.Length,
                    file.LastWriteTime,
                    matchedPlugin?.Name,
                    matchedPlugin?.Name));

                if (i % 10 == 0 || i == fileInfos.Count - 1)
                {
                    progress?.Report(new FileScanProgress(
                        FileScanPhase.ClassifyingFiles,
                        i + 1,
                        fileInfos.Count,
                        file.FullName,
                        $"({i + 1}/{fileInfos.Count}) {file.Name}"));
                }
            }

            progress?.Report(new FileScanProgress(
                FileScanPhase.Completed,
                fileInfos.Count,
                fileInfos.Count,
                null,
                "Scansione completata."));

            return result;
        }

        private static IEnumerable<FileInfo> EnumerateFiles(
            string rootFolder,
            string searchPattern,
            int maxDepth,
            IProgress<FileScanProgress>? progress,
            Action<string>? log,
            CancellationToken cancellationToken)
        {
            var stack = new Stack<(string Path, int Depth)>();
            stack.Push((rootFolder, 0));

            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (currentPath, depth) = stack.Pop();

                progress?.Report(new FileScanProgress(
                    FileScanPhase.ScanningDirectories,
                    null,
                    null,
                    currentPath,
                    currentPath));

                DirectoryInfo dirInfo;

                try
                {
                    dirInfo = new DirectoryInfo(currentPath);
                }
                catch (Exception ex)
                {
                    log?.Invoke($"Errore accesso cartella '{currentPath}': {ex.Message}");
                    continue;
                }

                FileInfo[] files;

                try
                {
                    files = dirInfo.GetFiles(searchPattern);
                }
                catch (Exception ex)
                {
                    log?.Invoke($"Impossibile enumerare i file in '{currentPath}': {ex.Message}");
                    files = [];
                }

                foreach (var file in files)
                    yield return file;

                if (depth >= maxDepth)
                    continue;

                DirectoryInfo[] subdirs;

                try
                {
                    subdirs = dirInfo.GetDirectories();
                }
                catch (Exception ex)
                {
                    log?.Invoke($"Impossibile enumerare le sottocartelle in '{currentPath}': {ex.Message}");
                    subdirs = [];
                }

                foreach (var subdir in subdirs)
                    stack.Push((subdir.FullName, depth + 1));
            }
        }

        private static async Task<IReportImporter?> FindMatchingPluginAsync(
            FileInfo file,
            IReadOnlyCollection<IReportImporter> importers,
            Action<string>? log,
            CancellationToken cancellationToken)
        {
            foreach (var importer in importers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (await importer.CanImportAsync(file.Name))
                        return importer;
                }
                catch (Exception ex)
                {
                    log?.Invoke(
                        $"Errore CanImportAsync plugin '{importer.Name}' per file '{file.Name}': {ex.Message}");
                }
            }

            return null;
        }
    }
}
