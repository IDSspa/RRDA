using RRDA.Core;
using RRDA.Plugins.Common;

namespace RRDA.Plugins.TC
{
    public sealed class TCImporter : BaseImporter
    {
        public override string Name => "TC";
        public override string Version => "1.0.1";
        public override string SupportedFileExtension => ".xlsx";
        public override IReadOnlyList<string> MatchingPatterns => [
            @"^Report_TC_AnD#[0-9]+-[0-9]+-[0-9]+-[0-9]+$",
            @"^Report_TC-AnD - #[0-9]+-[0-9]+$"
        ];
        public override ReportSubjectKind SubjectKind => ReportSubjectKind.Component;
        public override string SubjectKeyDefinedName => "Serial";
    }
}
