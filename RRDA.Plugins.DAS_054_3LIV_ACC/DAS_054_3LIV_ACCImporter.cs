using RRDA.Core;
using RRDA.Plugins.Common;

namespace RRDA.Plugins.DAS_054_3LIV_ACC
{
    public class DAS_054_3LIV_ACCImporter : BaseImporter
    {
        public override string Name => "DAS_054_3LIV_ACC";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override string MatchingPattern => "REPORT-DAS0054-ACCETTAZIONE_INT";
        public override ReportSubjectKind SubjectKind => ReportSubjectKind.Radar;
        public override string SubjectKeyDefinedName => "Serial";
    }
}
