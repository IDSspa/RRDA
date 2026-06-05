using RRDA.Core;

namespace RRDA.Plugins.Common
{
    public interface IPluginCatalog
    {
        PluginCatalogSnapshot Current { get; }

        PluginCatalogSnapshot Reload(
            string? configuredFolder,
            string applicationBaseDirectory);
    }

    public sealed record PluginCatalogSnapshot(
        string Folder,
        IReadOnlyList<IReportImporter> Plugins,
        IReadOnlyList<PluginLoadError> Errors,
        DateTime LoadedAtUtc)
    {
        public static PluginCatalogSnapshot Empty { get; } = new(
            string.Empty,
            [],
            [],
            DateTime.MinValue);
    }

    public sealed class PluginCatalog(IPluginService pluginService) : IPluginCatalog
    {
        private readonly object _syncRoot = new();
        private PluginCatalogSnapshot _current = PluginCatalogSnapshot.Empty;

        public PluginCatalogSnapshot Current
        {
            get
            {
                lock (_syncRoot)
                    return _current;
            }
        }

        public PluginCatalogSnapshot Reload(
            string? configuredFolder,
            string applicationBaseDirectory)
        {
            var folder = pluginService.ResolvePluginsFolder(
                configuredFolder,
                applicationBaseDirectory);
            var loadResult = pluginService.LoadPlugins(folder);
            var snapshot = new PluginCatalogSnapshot(
                folder,
                loadResult.Plugins,
                loadResult.Errors,
                DateTime.UtcNow);

            lock (_syncRoot)
                _current = snapshot;

            return snapshot;
        }
    }
}
