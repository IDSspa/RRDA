using RRDA.Core;
using RRDA.Plugins.Common;

namespace RRDA.Plugins.MAN_2Liv
{
    public sealed class Man2LivImporter : BaseImporter
    {
        public override string Name => "MAN_2Liv";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override IReadOnlyList<string> MatchingPatterns =>
        [
            "NCH_PAIPL_MAN_2Liv",
            "NCH_PAIPL_MAN_VERDE_2Liv"
        ];
        public override ReportSubjectKind SubjectKind => ReportSubjectKind.SubAssembly;
        public override string SubjectKeyDefinedName => "Serial";
    }
}
