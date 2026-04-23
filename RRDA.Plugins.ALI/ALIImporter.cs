using RRDA.Plugins.Common;

namespace RRDA.Plugins.ALI
{
    public sealed class ALIImporter : BaseImporter
    {
        public override string Name => "ALI";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override string MatchingPattern => "NCH_PAIPL_ALI";
        public override string EntityKind => "TestMeasurement";
    }
}
