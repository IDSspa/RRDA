using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace RRDA.Core.Validator;

public sealed class ImportBanListResolver
{
    private const string SchemaResourceName = "RRDA.Core.Validator.ImportBanList.xsd";
    private readonly IReadOnlyList<Regex> _definedNamePatterns;
    private readonly IReadOnlyList<Regex> _sheetPatterns;

    private ImportBanListResolver(
        IReadOnlyList<Regex> definedNamePatterns,
        IReadOnlyList<Regex> sheetPatterns)
    {
        _definedNamePatterns = definedNamePatterns;
        _sheetPatterns = sheetPatterns;
    }

    public static ImportBanListResolver Empty { get; } = new([], []);

    public static ImportBanListResolver Load(string? xmlPath)
    {
        if (string.IsNullOrWhiteSpace(xmlPath))
            return Empty;

        var fullPath = Path.GetFullPath(xmlPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("File della banlist di importazione non trovato.", fullPath);

        var document = LoadAndValidate(fullPath);
        var root = document.Root
            ?? throw new InvalidDataException("La banlist di importazione non contiene un elemento root.");
        var regexOptions = RegexOptions.CultureInvariant;
        if (!(bool)root.Attribute("caseSensitive")!)
            regexOptions |= RegexOptions.IgnoreCase;

        return new ImportBanListResolver(
            CreatePatterns(root.Element("DefinedNames"), regexOptions),
            CreatePatterns(root.Element("Sheets"), regexOptions));
    }

    public bool IsDefinedNameExcluded(string? definedName) =>
        IsExcluded(definedName, _definedNamePatterns);

    public bool IsSheetExcluded(string? sheetName) =>
        IsExcluded(sheetName, _sheetPatterns);

    private static IReadOnlyList<Regex> CreatePatterns(XElement? container, RegexOptions options) =>
        container?.Elements("Pattern")
            .Select(pattern => CreateGlobRegex(pattern.Value, options))
            .ToList()
        ?? [];

    private static bool IsExcluded(string? value, IReadOnlyList<Regex> patterns)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return patterns.Any(pattern => pattern.IsMatch(value));
    }

    private static XDocument LoadAndValidate(string xmlPath)
    {
        try
        {
            using var schemaStream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(SchemaResourceName)
                ?? throw new InvalidOperationException("Schema XSD ImportBanList non disponibile.");
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
                    $"ImportBanList XML non valido: {args.Message}",
                    args.Exception);

            using var reader = XmlReader.Create(xmlPath, settings);
            return XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (Exception ex) when (ex is XmlException or XmlSchemaException)
        {
            throw new InvalidDataException(
                $"Impossibile caricare ImportBanList da '{xmlPath}': {ex.Message}",
                ex);
        }
    }

    private static Regex CreateGlobRegex(string pattern, RegexOptions options)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new InvalidDataException("ImportBanList contiene un pattern vuoto.");

        var expression = $"^{Regex.Escape(pattern).Replace(@"\*", ".*")}$";
        return new Regex(expression, options);
    }
}
