using RRDA.Core;
using RRDA.Plugins.Common;

namespace RRDA.Plugins.TC
{
    public sealed class TCImporter : BaseImporter
    {
        public override string Name => "TC";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override string MatchingPattern => "Report_TC-AnD - ";
        public override ReportSubjectKind SubjectKind => ReportSubjectKind.Component;
        public override string SubjectKeyDefinedName => "Serial";


    }
}
