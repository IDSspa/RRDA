using System.Runtime.Loader;

namespace RRDA.Core
{
    public class PluginLoader
    {
        public IEnumerable<IReportImporter> LoadPlugins(string pluginFolder)
        {
            var dlls = Directory.GetFiles(pluginFolder, "*.dll");

            var importers = new List<IReportImporter>();

            foreach (var dll in dlls)
            {
                var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
                var types = asm.GetTypes().Where(t => typeof(IReportImporter).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var t in types)
                {
                    if (Activator.CreateInstance(t) is IReportImporter imp)
                        importers.Add(imp);
                }
            }

            return importers;
        }
    }
}
