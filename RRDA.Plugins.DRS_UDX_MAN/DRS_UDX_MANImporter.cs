using RRDA.Plugins.Common;

namespace RRDA.Plugins.DRS_UDX_MAN
{
    public class DRS_UDX_MANImporter : BaseImporter
    {
        public override string Name => "DRS_UDX_MAN";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override string MatchingPattern => "NCH_DRS_UDX_MAN";
        public override string EntityKind => "TestMeasurement";
    }
}
