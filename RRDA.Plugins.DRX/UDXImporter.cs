using RRDA.Core;
using RRDA.Plugins.Common;

namespace RRDA.Plugins.DRX
{
    public sealed class DRXImporter : BaseImporter
    {
        public override string Name => "DRX";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override string MatchingPattern => "NCH_2022_002_DRX_MAN";
        public override ReportSubjectKind SubjectKind => ReportSubjectKind.Component;
        public override string SubjectKeyDefinedName => "Serial";
    }
}
