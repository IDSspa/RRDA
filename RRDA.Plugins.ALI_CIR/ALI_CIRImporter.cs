using RRDA.Plugins.Common;

namespace RRDA.Plugins.ALI_CIR
{
    public sealed class ALI_CIRImporter : BaseImporter
    {
        public override string Name => "ALI_CIR";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override string MatchingPattern => "NCH_2023_005_10_LXOD-RS_ALI_CIR";
        public override string EntityKind => "TestMeasurement";
    }
}
