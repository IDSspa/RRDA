using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using System.Reflection;
using System.Text.RegularExpressions;

namespace RRDA.Core;

public sealed class ReportFileBanListResolver
{
    private const string SchemaResourceName = "RRDA.Core.ReportFileBanList.xsd";
    private readonly IReadOnlyList<Regex> _fileNamePatterns;

    private ReportFileBanListResolver(IReadOnlyList<Regex> fileNamePatterns)
    {
        _fileNamePatterns = fileNamePatterns;
    }

    public static ReportFileBanListResolver Empty { get; } = new([]);

    /// <summary>
    /// Carica la banlist da un file XML. Se il file non esiste o non è accessibile,
    /// restituisce un resolver vuoto (tutti i file sono ammessi).
    /// </summary>
    public static ReportFileBanListResolver Load(string? xmlPath)
    {
        if (string.IsNullOrWhiteSpace(xmlPath))
            return Empty;

        var fullPath = Path.GetFullPath(xmlPath);

        // Se il file non esiste, non è un errore: permetti tutti i file
        if (!File.Exists(fullPath))
            return Empty;

        try
        {
            var document = LoadAndValidate(fullPath);
            return ParseDocument(document);
        }
        catch (Exception ex) when (ex is XmlException or XmlSchemaException or InvalidDataException)
        {
            // Log l'errore ma non fallire: permetti tutti i file
            System.Diagnostics.Debug.WriteLine(
                $"Avviso: impossibile caricare ReportFileBanList da '{fullPath}': {ex.Message}. " +
                $"Tutti i file saranno ammessi.");
            return Empty;
        }
    }

    public bool IsFileNameExcluded(string? fileName) =>
        IsExcluded(fileName, _fileNamePatterns);

    private static ReportFileBanListResolver ParseDocument(XDocument document)
    {
        var root = document.Root
            ?? throw new InvalidDataException("La banlist di report non contiene un elemento root.");

        var regexOptions = RegexOptions.CultureInvariant;
        if (!(bool)root.Attribute("caseSensitive")!)
            regexOptions |= RegexOptions.IgnoreCase;

        return new ReportFileBanListResolver(
            CreatePatterns(root.Element("FileNames"), regexOptions));
    }

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
            // Prova a caricare lo schema come risorsa embedded
            XmlSchemaSet schemas = new();

            using (var schemaStream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(SchemaResourceName))
            {
                if (schemaStream != null)
                {
                    schemas.Add(null, XmlReader.Create(schemaStream));
                }
                else
                {
                    // Schema non trovato come risorsa: procedi senza validazione
                    // (il file sarà comunque caricato, solo non validato)
                    System.Diagnostics.Debug.WriteLine(
                        $"Avviso: schema XSD '{SchemaResourceName}' non trovato come risorsa embedded. " +
                        $"Il file XML sarà caricato senza validazione.");

                    return XDocument.Load(xmlPath, LoadOptions.SetLineInfo);
                }
            }

            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                ValidationType = ValidationType.Schema,
                Schemas = schemas
            };

            settings.ValidationEventHandler += (_, args) =>
                throw new XmlSchemaValidationException(
                    $"ReportFileBanList XML non valido: {args.Message}",
                    args.Exception);

            using var reader = XmlReader.Create(xmlPath, settings);
            return XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (Exception ex) when (ex is XmlException or XmlSchemaException)
        {
            throw new InvalidDataException(
                $"Impossibile caricare ReportFileBanList da '{xmlPath}': {ex.Message}",
                ex);
        }
    }

    private static Regex CreateGlobRegex(string pattern, RegexOptions options)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new InvalidDataException("ReportFileBanList contiene un pattern vuoto.");

        var expression = $"^{Regex.Escape(pattern).Replace(@"\*", ".*")}$";
        return new Regex(expression, options);
    }
}