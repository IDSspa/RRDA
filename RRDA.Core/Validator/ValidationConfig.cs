using DocumentFormat.OpenXml.Spreadsheet;
using System.Globalization;
using System.Xml.Linq;

namespace RRDA.Core.Validator
{
    public class ValidationConfig
    {
        public bool FailOnError { get; set; } = true;
        public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;
        public List<FieldMapping> Mappings { get; set; } = [];
        public List<FieldRule> FieldRules { get; set; } = [];
        public List<Sheet> Sheets { get; set; } = [];
        public List<RowRule> RowRules { get; set; } = [];
        public static ValidationConfig Load(Stream xmlStream)
        {
            var doc = XDocument.Load(xmlStream);

            var root = doc.Root!;

            var cfg = new ValidationConfig
            {
                FailOnError = (bool?)root.Attribute("failOnError") ?? true
            };

            var cultureName = (string?)root.Attribute("culture");
            if (!string.IsNullOrEmpty(cultureName)) 
                cfg.Culture = new CultureInfo(cultureName);

            var sheets = root.Element("Sheets")?.Elements("Sheet") ?? [];

            foreach (var s in sheets)
            {
                var sheetName = s.Attribute("Name");
                if (sheetName is not null) 
                    cfg.Sheets.Add(new Sheet { Name = sheetName.Value });
            }

            var mappings = root.Element("FieldMappings")?.Elements("Map") ?? [];
            
            foreach (var m in mappings)
            {
                var definedName = (string?)m.Attribute("definedName") ?? string.Empty;
                var field = (string?)m.Attribute("field") ?? string.Empty;
                cfg.Mappings.Add(new FieldMapping { 
                    DefinedName = definedName, 
                    Field = field 
                });
            }

            var field_rules = root.Element("FieldRules")?.Elements("Field") ?? [];
            
            foreach (var f in field_rules)
            {
                var nameAttr = f.Attribute("definedName");

                var fr = new FieldRule
                {
                    DefinedName = nameAttr is not null ? nameAttr.Value : string.Empty,
                    Required = (bool?)f.Attribute("required") ?? false
                };

                var typeStr = (string?)f.Attribute("type") ?? "string";

                fr.DataType = typeStr.ToLower() switch
                {
                    "int" => FieldDataType.Int,
                    "double" => FieldDataType.Double,
                    "datetime" => FieldDataType.DateTime,
                    "bool" => FieldDataType.Bool,
                    _ => FieldDataType.String
                };

                fr.Pattern = (string?)f.Attribute("pattern");
                fr.Format = (string?)f.Attribute("format");

                if (double.TryParse((string?)f.Attribute("min"),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var min))
                        fr.Min = min;
                
                if (double.TryParse((string?)f.Attribute("max"),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var max))
                    fr.Max = max;
                
                if (int.TryParse((string?)f.Attribute("maxLength"), out var ml))
                    fr.MaxLength = ml;

                fr.Unit = (string?)f.Attribute("unit");

                var rangeNode = f.Element("Range");
                if (rangeNode != null)
                {
                    fr.Range = new Range
                    {
                        row_count = (int?)rangeNode.Attribute("rowCount") ?? 0,
                        col_count = (int?)rangeNode.Attribute("colCount") ?? 0
                    };
                }

                cfg.FieldRules.Add(fr);
            }

            var rowRules = root.Element("RowRules")?.Elements("Rule") ?? [];
            
            foreach (var r in rowRules)
            {
                cfg.RowRules.Add(new RowRule
                {
                    Name = (string?)r.Attribute("name") ?? "",
                    Severity = (string?)r.Attribute("severity") ?? "error",
                    ConditionExpression = (string)r.Element("Condition")!,
                    Message = (string?)r.Element("Message") ?? ""
                });
            }

            return cfg;
        }
    }
}