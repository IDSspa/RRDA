using RRDA.Core;
using System;
using System.Collections.Generic;

namespace RRDA.Plugins.Common;

public static class ExcelRowMapper
{
    public static ReportEntityDto Map(
        IDictionary<string, string> row,
        ValidationConfig cfg,
        string entityKind,
        Func<IDictionary<string, string>, string> keySelector)
    {
        var dto = new ReportEntityDto
        {
            EntityKind = entityKind,
            Key = keySelector(row)
        };

        foreach (var (excelCol, value) in row)
        {
            if (!cfg.FieldMappings.TryGetValue(excelCol, out var field))
                continue;

            dto.Properties[field] = value;
        }

        return dto;
    }
}