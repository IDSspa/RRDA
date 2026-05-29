using RRDA.Core;
using RRDA.Plugins.Common;

namespace RRDA.Plugins.DAS_054_3LIV
{
    public class DAS_054_3LIVImporter : BaseImporter
    {
        public override string Name => "DAS_054_3LIV";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override string MatchingPattern => "REPORT-DAS0054-ACCETTAZIONE";
        public override ReportSubjectKind SubjectKind => ReportSubjectKind.Radar;
    }
}
