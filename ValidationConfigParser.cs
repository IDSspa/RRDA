using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace RRDA.Plugins.Common;

public sealed class ValidationConfig
{
    public Dictionary<string, string> FieldMappings { get; } = [];
}

public static class ValidationConfigParser
{
    public static ValidationConfig Parse(Stream xml)
    {
        var doc = XDocument.Load(xml);
        var cfg = new ValidationConfig();

        var maps = doc.Root?
            .Element("FieldMappings")?
            .Elements("Map") ?? [];

        foreach (var m in maps)
        {
            cfg.FieldMappings[
                m.Attribute("ExcelColumn")!.Value
            ] = m.Attribute("FieldName")!.Value;
        }

        return cfg;
    }
}