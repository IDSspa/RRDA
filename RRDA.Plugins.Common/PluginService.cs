using System.Reflection;
using System.Runtime.Loader;
using RRDA.Core;

namespace RRDA.Plugins.Common
{
    public interface IPluginService
    {
        string ResolvePluginsFolder(string? configuredFolder, string applicationBaseDirectory);
        PluginLoadResult LoadPlugins(string pluginFolder);
    }

    public sealed record PluginLoadError(string Source, string Message);

    public sealed record PluginLoadResult(
        IReadOnlyList<IReportImporter> Plugins,
        IReadOnlyList<PluginLoadError> Errors);

    public sealed class PluginService : IPluginService
    {
        public string ResolvePluginsFolder(string? configuredFolder, string applicationBaseDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);

            if (!string.IsNullOrWhiteSpace(configuredFolder))
                return Path.GetFullPath(configuredFolder, applicationBaseDirectory);

#if DEBUG
            for (var current = new DirectoryInfo(applicationBaseDirectory);
                 current != null;
                 current = current.Parent)
            {
                var developmentFolder = Path.Combine(
                    current.FullName,
                    "artifacts",
                    "plugins",
                    "Debug",
                    "net8.0");

                if (Directory.Exists(developmentFolder))
                    return developmentFolder;
            }
#endif

            return Path.Combine(applicationBaseDirectory, "plugins");
        }

        public PluginLoadResult LoadPlugins(string pluginFolder)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pluginFolder);

            var plugins = new List<IReportImporter>();
            var errors = new List<PluginLoadError>();

            if (!Directory.Exists(pluginFolder))
            {
                errors.Add(new PluginLoadError(pluginFolder, "Cartella plugins non trovata."));
                return new PluginLoadResult(plugins, errors);
            }

            string[] dlls;
            try
            {
                dlls = Directory.GetFiles(pluginFolder, "RRDA.Plugins.*.dll")
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex)
            {
                errors.Add(new PluginLoadError(pluginFolder, ex.Message));
                return new PluginLoadResult(plugins, errors);
            }

            var pluginNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dll in dlls)
                LoadPluginsFromAssembly(dll, plugins, pluginNames, errors);

            return new PluginLoadResult(plugins, errors);
        }

        private static void LoadPluginsFromAssembly(
            string assemblyPath,
            ICollection<IReportImporter> plugins,
            ISet<string> pluginNames,
            ICollection<PluginLoadError> errors)
        {
            try
            {
                var assembly = LoadAssembly(assemblyPath);
                foreach (var type in GetLoadablePluginTypes(assembly, assemblyPath, errors))
                {
                    try
                    {
                        if (Activator.CreateInstance(type) is not IReportImporter plugin)
                            continue;

                        if (!pluginNames.Add(plugin.Name))
                        {
                            errors.Add(new PluginLoadError(
                                assemblyPath,
                                $"Plugin duplicato con nome '{plugin.Name}'."));
                            continue;
                        }

                        plugins.Add(plugin);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new PluginLoadError(
                            assemblyPath,
                            $"Impossibile creare il plugin '{type.FullName}': {ex.Message}"));
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(new PluginLoadError(assemblyPath, ex.Message));
            }
        }

        private static Assembly LoadAssembly(string assemblyPath)
        {
            var fullPath = Path.GetFullPath(assemblyPath);
            var assemblyName = AssemblyName.GetAssemblyName(fullPath);
            var loadedAssembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(
                assembly => AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), assemblyName));

            return loadedAssembly ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
        }

        private static IEnumerable<Type> GetLoadablePluginTypes(
            Assembly assembly,
            string assemblyPath,
            ICollection<PluginLoadError> errors)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.OfType<Type>().ToArray();
                foreach (var loaderException in ex.LoaderExceptions.Where(exception => exception != null))
                    errors.Add(new PluginLoadError(assemblyPath, loaderException!.Message));
            }

            return types.Where(type =>
                typeof(IReportImporter).IsAssignableFrom(type)
                && !type.IsInterface
                && !type.IsAbstract);
        }
    }
}
