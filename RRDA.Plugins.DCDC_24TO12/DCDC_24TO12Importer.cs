using RRDA.Plugins.Common;

namespace RRDA.Plugins.DCDC_24TO12
{
    public sealed class DCDC_24TO12Importer : BaseImporter
    {
        public override string Name => "DCDC_24TO12";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override string MatchingPattern => "NCH_DCDC_24TO12_v1.0";
        public override string EntityKind => "TestMeasurement";
    }
}
