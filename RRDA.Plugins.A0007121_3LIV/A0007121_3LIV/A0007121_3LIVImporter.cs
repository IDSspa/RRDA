using RRDA.Core;
using RRDA.Plugins.Common;

namespace RRDA.Plugins.A0007121_3LIV
{
    public sealed class A0007121_3LIVImporter : BaseImporter
    {
        public override string Name => "A0007121_3LIV";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override IReadOnlyList<string> MatchingPatterns => [
            @"^NCH_2022_006_A0007121_Accettazione#[0-9]+$"
        ];
        public override ReportSubjectKind SubjectKind => ReportSubjectKind.Component;
        public override string SubjectKeyDefinedName => "Serial";
    }
}
