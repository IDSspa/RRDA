using RRDA.Core;
using RRDA.Plugins.Common;

namespace RRDA.Plugins.DIG_31
{
    public sealed class DIG31Importer : BaseImporter
    {
        public override string Name => "DIG_31";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override string MatchingPattern => "NCH_RSR_DIG_3_1";
        public override ReportSubjectKind SubjectKind => ReportSubjectKind.Component;
    }
}
