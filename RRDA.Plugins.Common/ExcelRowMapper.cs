using RRDA.Core;
using RRDA.Core.Validator;

namespace RRDA.Plugins.Common;

public static class ExcelRowMapper
{
    public static ReportEntityDto Map(
        IDictionary<string, string> row,
        ValidationConfig cfg,
        string entityKind,
        Func<IDictionary<string, string>, string> keySelector)
    {
        if (row is null) throw new ArgumentNullException(nameof(row));
        if (cfg is null) throw new ArgumentNullException(nameof(cfg));
        if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
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