using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace RRDA.Core.Validator;

public sealed class UnitMappingResolver
{
    private const string SchemaResourceName = "RRDA.Core.Validator.UnitMappings.xsd";
    private readonly IReadOnlyList<UnitMappingRule> _rules;

    private UnitMappingResolver(IReadOnlyList<UnitMappingRule> rules)
    {
        _rules = rules;
    }

    public static UnitMappingResolver Empty { get; } = new([]);

    public static UnitMappingResolver Load(string? xmlPath)
    {
        if (string.IsNullOrWhiteSpace(xmlPath))
            return Empty;

        var fullPath = Path.GetFullPath(xmlPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("File dei mapping delle unità di misura non trovato.", fullPath);

        var document = LoadAndValidate(fullPath);
        var root = document.Root
            ?? throw new InvalidDataException("Il file dei mapping delle unità di misura non contiene un elemento root.");
        var caseSensitive = (bool)root.Attribute("caseSensitive")!;
        var regexOptions = RegexOptions.CultureInvariant;
        if (!caseSensitive)
            regexOptions |= RegexOptions.IgnoreCase;

        var rules = root.Elements("Rule")
            .SelectMany((rule, ruleOrder) =>
            {
                var unit = (string)rule.Attribute("unit")!;
                var priority = (int)rule.Attribute("priority")!;

                return rule.Elements("Pattern")
                    .Select((pattern, patternOrder) => new UnitMappingRule(
                        unit,
                        priority,
                        ruleOrder,
                        patternOrder,
                        CreateGlobRegex(pattern.Value, regexOptions)));
            })
            .OrderByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.RuleOrder)
            .ThenBy(rule => rule.PatternOrder)
            .ToList();

        return new UnitMappingResolver(rules);
    }

    public string? InferUnit(string? definedName)
    {
        if (string.IsNullOrWhiteSpace(definedName))
            return null;

        return _rules.FirstOrDefault(rule => rule.Pattern.IsMatch(definedName))?.Unit;
    }

    private static XDocument LoadAndValidate(string xmlPath)
    {
        try
        {
            using var schemaStream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(SchemaResourceName)
                ?? throw new InvalidOperationException("Schema XSD UnitMappings non disponibile.");
            var schemas = new XmlSchemaSet();
            schemas.Add(null, XmlReader.Create(schemaStream));

            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                ValidationType = ValidationType.Schema,
                Schemas = schemas
            };
            settings.ValidationEventHandler += (_, args) =>
                throw new XmlSchemaValidationException(
                    $"UnitMappings XML non valido: {args.Message}",
                    args.Exception);

            using var reader = XmlReader.Create(xmlPath, settings);
            return XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (Exception ex) when (ex is XmlException or XmlSchemaException)
        {
            throw new InvalidDataException(
                $"Impossibile caricare UnitMappings da '{xmlPath}': {ex.Message}",
                ex);
        }
    }

    private static Regex CreateGlobRegex(string pattern, RegexOptions options)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new InvalidDataException("UnitMappings contiene un pattern vuoto.");

        var expression = $"^{Regex.Escape(pattern).Replace(@"\*", ".*")}$";
        return new Regex(expression, options);
    }

    private sealed record UnitMappingRule(
        string Unit,
        int Priority,
        int RuleOrder,
        int PatternOrder,
        Regex Pattern);
}
