using RRDA.Core.Validator;
using RRDA.Core;

namespace RRDA.Plugins.Common
{
    public static class ExcelRowMapper
    {
        public static ReportEntityDto Map(
            IDictionary<string, string> row,
            ValidationConfig cfg,
            string entityKind,
            Func<IDictionary<string, string>, string> keySelector)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentNullException.ThrowIfNull(cfg);
            ArgumentNullException.ThrowIfNull(keySelector);
            if (string.IsNullOrWhiteSpace(entityKind)) throw new ArgumentNullException(nameof(entityKind));

            var key = keySelector(row) ?? string.Empty;

            var dto = new ReportEntityDto
            {
                EntityKind = entityKind,
                Key = key
            };

            foreach (var kv in row)
            {
                var excelCol = kv.Key;
                var value = kv.Value ?? string.Empty;

                if (string.IsNullOrEmpty(excelCol))
                    continue;

                // Cerca il mapping corrispondente nella lista
                var mapping = cfg.Mappings?.Find(m => string.Equals(m.DefinedName, excelCol, StringComparison.OrdinalIgnoreCase));
                if (mapping == null || string.IsNullOrWhiteSpace(mapping.Field))
                    continue;

                dto.Properties[mapping.Field] = value;
            }

            return dto;
        }
    }
}