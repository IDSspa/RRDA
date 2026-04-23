using RRDA.Plugins.Common;

namespace RRDA.Plugins.RCB
{
    public class RCBImporter : BaseImporter
    {
        public override string Name => "RCB";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override string MatchingPattern => "NCH_PAIPL_RCB";
        public override string EntityKind => "TestMeasurement";
    }
}
