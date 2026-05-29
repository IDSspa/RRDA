using RRDA.Core;
using RRDA.Plugins.Common;

namespace RRDA.Plugins.Dummy
{
    public class DummyImporter : BaseImporter
    {
        public override string Name => "Dummy";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override string MatchingPattern => "Dummy1";
        public override ReportSubjectKind SubjectKind => ReportSubjectKind.Component;
    }
}
