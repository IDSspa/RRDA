using RRDA.Core;
using RRDA.Plugins.Common;

namespace RRDA.Plugins.DIG_PM
{
    public sealed class DIG_PMImporter : BaseImporter
    {
        public override string Name => "DIG_PM";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override string MatchingPattern => "NCH_2022_007_10_LXOD-RS_DRX_DIG_PM";
        public override ReportSubjectKind SubjectKind => ReportSubjectKind.Component;
        public override string SubjectKeyDefinedName => "Serial";
    }
}
