using RRDA.Plugins.Common;

namespace RRDA.Plugins.DIG_50
{
    public sealed class DIG50Importer : BaseImporter
    {
        public override string Name => "DIG_50";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override string MatchingPattern => "NCH_2022_008_10_LXOD-RS_DIG_50";
        public override string EntityKind => "TestMeasurement";
    }
}
