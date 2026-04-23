using RRDA.Plugins.Common;

namespace RRDA.Plugins.SBX
{
    public sealed class SBXImporter : BaseImporter
    {
        public override string Name => "SBX";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override string MatchingPattern => "NCH_2022_004_1_3_LXOD-RS_SBX";
        public override string EntityKind => "TestMeasurement";
    }
}
