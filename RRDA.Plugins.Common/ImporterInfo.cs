namespace RRDA.Plugins.Common
{
    public sealed class ImporterInfo
    {
        public string Name { get; init; } = "";
        public string Version { get; init; } = "";
        public string Extension { get; init; } = "";
        public IReadOnlyList<string> Patterns { get; init; } = [];
    }
}
