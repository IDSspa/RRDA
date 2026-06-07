using RRDA.Core;
using RRDA.Plugins.Common;

namespace RRDA.Plugins.MAN_2Liv
{
    public sealed class Man2LivImporter : BaseImporter
    {
        public override string Name => "MAN_2Liv";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override string MatchingPattern => "NCH_PAIPL_MAN_2Liv";
        public override ReportSubjectKind SubjectKind => ReportSubjectKind.SubAssembly;
        public override string SubjectKeyDefinedName => "Serial";
    }
}
