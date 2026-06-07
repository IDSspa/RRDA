using RRDA.Core;
using RRDA.Plugins.Common;

namespace RRDA.Plugins.UDX
{
    public sealed class UDXImporter : BaseImporter
    {
        public override string Name => "UDX";
        public override string Version => "1.0.0";
        public override string SupportedFileExtension => ".xlsx";
        public override string MatchingPattern => "NCH_RSR_UDX_1_2";
        public override ReportSubjectKind SubjectKind => ReportSubjectKind.Component;
        public override string SubjectKeyDefinedName => "Serial";
    }
}
