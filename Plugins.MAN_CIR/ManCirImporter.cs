using RRDA.Core;
using RRDA.Plugins.Common;

namespace RRDA.Plugins.MAN_CIR
{
    public sealed class ManCirImporter : BaseImporter
    {
        public override string Name => "MAN_CIR";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override string MatchingPattern => "NCH_PAIPL_MAN_CIR";
        public override ReportSubjectKind SubjectKind => ReportSubjectKind.Component;
        public override string SubjectKeyDefinedName => "Serial";
    }
}
