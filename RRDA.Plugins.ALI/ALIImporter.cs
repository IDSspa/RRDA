using RRDA.Core;
using RRDA.Plugins.Common;

namespace RRDA.Plugins.ALI
{
    public sealed class ALIImporter : BaseImporter
    {
        public override string Name => "ALI";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override IReadOnlyList<string> MatchingPatterns => [
            @"^NCH_PAIPL_ALI#[0-9]+$"
        ];
        public override ReportSubjectKind SubjectKind => ReportSubjectKind.Component;
        public override string SubjectKeyDefinedName => "Serial";
    }
}
