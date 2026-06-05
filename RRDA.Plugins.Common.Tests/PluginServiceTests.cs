using RRDA.Plugins.Common;
using RRDA.Plugins.Dummy;
using Xunit;

namespace RRDA.Plugins.Common.Tests;

public sealed class PluginServiceTests
{
    private readonly PluginService _service = new();

    [Fact]
    public void ResolvePluginsFolder_ResolvesRelativeConfiguredPathAgainstApplicationDirectory()
    {
        using var directory = new TemporaryDirectory();

        var result = _service.ResolvePluginsFolder("configured-plugins", directory.Path);

        Assert.Equal(Path.Combine(directory.Path, "configured-plugins"), result);
    }

    [Fact]
    public void LoadPlugins_ReturnsErrorWhenFolderDoesNotExist()
    {
        using var directory = new TemporaryDirectory();
        var missingFolder = Path.Combine(directory.Path, "missing");

        var result = _service.LoadPlugins(missingFolder);

        Assert.Empty(result.Plugins);
        Assert.Single(result.Errors);
        Assert.Equal(missingFolder, result.Errors[0].Source);
    }

    [Fact]
    public void LoadPlugins_ContinuesAfterInvalidAssembly()
    {
        using var directory = new TemporaryDirectory();
        CopyDummyPlugin(directory.Path, "RRDA.Plugins.Dummy.dll");
        File.WriteAllText(Path.Combine(directory.Path, "RRDA.Plugins.Invalid.dll"), "not an assembly");

        var result = _service.LoadPlugins(directory.Path);

        var plugin = Assert.Single(result.Plugins);
        Assert.Equal("Dummy", plugin.Name);
        Assert.Single(result.Errors);
        Assert.EndsWith("RRDA.Plugins.Invalid.dll", result.Errors[0].Source);
    }

    [Fact]
    public void LoadPlugins_KeepsFirstPluginWhenNamesAreDuplicated()
    {
        using var directory = new TemporaryDirectory();
        CopyDummyPlugin(directory.Path, "RRDA.Plugins.Dummy.First.dll");
        CopyDummyPlugin(directory.Path, "RRDA.Plugins.Dummy.Second.dll");

        var result = _service.LoadPlugins(directory.Path);

        Assert.Single(result.Plugins);
        var error = Assert.Single(result.Errors);
        Assert.Contains("duplicato", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDummyPlugin(string targetFolder, string fileName)
    {
        File.Copy(
            typeof(DummyImporter).Assembly.Location,
            Path.Combine(targetFolder, fileName));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"RRDA.PluginService.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
