using NCalc;
using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RRDA.Core.Validator;

/// <summary>
/// Valuta i campi calcolati consentiti dal validatore. Le formule sono valutate
/// esclusivamente durante l'importazione e il risultato viene materializzato.
/// </summary>
public static class CalculatedFieldEvaluator
{
    private static readonly HashSet<string> FunctionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Abs", "Log", "Pow", "Min", "Max", "Avg", "Sum", "Count", "StdDev"
    };

    private static readonly Regex Identifier = new(
        @"\b[A-Za-z_][A-Za-z0-9_]*\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex FunctionCall = new(
        @"\b(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static void AppendCalculatedEntities(
        ICollection<ReportEntityDto> entities,
        ValidationConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(config);
        if (config.CalculatedFields.Count == 0)
            return;

        // I FieldRules descrivono dati presenti nel report e hanno sempre
        // precedenza: un campo calcolato omonimo non deve sovrascriverli.
        var fieldRuleNames = config.FieldRules
            .Select(field => field.DefinedName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var definitions = new Dictionary<string, CalculatedFieldRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in config.CalculatedFields)
        {
            if (fieldRuleNames.Contains(rule.Alias))
                continue;
            if (!IsValidIdentifier(rule.Alias))
                throw new InvalidDataException($"L'alias del campo calcolato '{rule.Alias}' non è un identificatore valido.");
            if (string.IsNullOrWhiteSpace(rule.Formula))
                throw new InvalidDataException($"Il campo calcolato '{rule.Alias}' non ha una formula.");
            if (!definitions.TryAdd(rule.Alias, rule))
                throw new InvalidDataException($"Il campo calcolato '{rule.Alias}' è dichiarato più volte.");
        }

        var source = BuildSourceValues(entities, config.Culture);
        foreach (var alias in definitions.Keys)
        {
            if (source.Scalars.ContainsKey(alias) || source.Ranges.ContainsKey(alias))
            {
                throw new InvalidDataException(
                    $"L'alias del campo calcolato '{alias}' è già usato da un campo importato.");
            }
        }
        var dependencies = definitions.ToDictionary(
            pair => pair.Key,
            pair => ExtractIdentifiers(pair.Value.Formula)
                .Where(definitions.ContainsKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            StringComparer.OrdinalIgnoreCase);
        var evaluationOrder = TopologicalOrder(dependencies);

        foreach (var alias in evaluationOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rule = definitions[alias];
            ValidateFormula(rule, source, definitions);
            var result = Evaluate(rule, source);

            if (rule.DataType == FieldDataType.Int && result != Math.Truncate(result))
            {
                throw new InvalidDataException(
                    $"Il campo calcolato '{rule.Alias}' ha tipo int ma la formula produce '{result.ToString(CultureInfo.InvariantCulture)}'.");
            }

            source.Scalars[rule.Alias] = result;
            entities.Add(new ReportEntityDto
            {
                EntityKind = "Calculated",
                Key = rule.Alias,
                Properties = new Dictionary<string, string>
                {
                    ["name"] = rule.Alias,
                    ["value"] = result.ToString("R", CultureInfo.InvariantCulture),
                    ["unit"] = rule.Unit ?? string.Empty,
                    ["row_index"] = "-1",
                    ["col_index"] = "-1",
                    ["is_subject_key"] = "0",
                    ["data_type"] = BaseDataType(rule.DataType)
                }
            });
        }
    }

    private static SourceValues BuildSourceValues(
        IEnumerable<ReportEntityDto> entities,
        CultureInfo culture)
    {
        var result = new SourceValues();
        foreach (var entity in entities)
        {
            if (entity.Properties is null || !entity.Properties.TryGetValue("value", out var rawValue))
                continue;

            var name = entity.Properties.TryGetValue("name", out var propertyName)
                && !string.IsNullOrWhiteSpace(propertyName)
                    ? propertyName
                    : entity.Key;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!TryParseDouble(rawValue, culture, out var value))
            {
                result.InvalidNames.Add(name);
                continue;
            }

            var isRange = entity.Properties.TryGetValue("row_index", out var rowIndex)
                && !string.Equals(rowIndex, "-1", StringComparison.Ordinal);
            if (isRange)
            {
                if (!result.Ranges.TryGetValue(name, out var values))
                {
                    values = [];
                    result.Ranges.Add(name, values);
                }
                values.Add(value);
            }
            else if (!result.Scalars.TryAdd(name, value))
            {
                throw new InvalidDataException($"Il campo sorgente '{name}' è presente più volte.");
            }
        }
        return result;
    }

    private static void ValidateFormula(
        CalculatedFieldRule rule,
        SourceValues source,
        IReadOnlyDictionary<string, CalculatedFieldRule> definitions)
    {
        foreach (Match match in FunctionCall.Matches(rule.Formula))
        {
            if (!FunctionNames.Contains(match.Groups["name"].Value))
                throw new InvalidDataException(
                    $"La formula del campo '{rule.Alias}' usa la funzione non consentita '{match.Groups["name"].Value}'.");
        }

        foreach (var identifier in ExtractIdentifiers(rule.Formula))
        {
            if (source.InvalidNames.Contains(identifier)
                || (!source.Scalars.ContainsKey(identifier)
                && !source.Ranges.ContainsKey(identifier)
                && !definitions.ContainsKey(identifier)))
            {
                throw new InvalidDataException(
                    $"La formula del campo '{rule.Alias}' richiede l'input '{identifier}', assente o non numerico.");
            }
        }
    }

    private static double Evaluate(CalculatedFieldRule rule, SourceValues source)
    {
        try
        {
            var expression = new Expression(NormalizeFunctionNames(rule.Formula));
            foreach (var scalar in source.Scalars)
                expression.Parameters[scalar.Key] = scalar.Value;
            foreach (var range in source.Ranges)
                expression.Parameters[range.Key] = range.Value;

            expression.Functions["Abs"] = args => Math.Abs(One(args, "Abs"));
            expression.Functions["Log"] = args =>
            {
                var values = Exact(args, "Log", 2);
                if (values[0] <= 0 || values[0] == 1 || values[1] <= 0)
                    throw new InvalidDataException("Log richiede base > 0 e diversa da 1 e value > 0.");
                return Math.Log(values[1], values[0]);
            };
            expression.Functions["Pow"] = args =>
            {
                var values = Exact(args, "Pow", 2);
                return Math.Pow(values[0], values[1]);
            };
            expression.Functions["Min"] = args => Aggregate(args, "Min", values => values.Min());
            expression.Functions["Max"] = args => Aggregate(args, "Max", values => values.Max());
            expression.Functions["Avg"] = args => Aggregate(args, "Avg", values => values.Average());
            expression.Functions["Sum"] = args => Aggregate(args, "Sum", values => values.Sum());
            expression.Functions["Count"] = args => Aggregate(args, "Count", values => values.Count);
            expression.Functions["StdDev"] = args => Aggregate(args, "StdDev", StandardDeviation);

            var rawResult = expression.Evaluate();
            var result = Convert.ToDouble(rawResult, CultureInfo.InvariantCulture);
            if (double.IsNaN(result) || double.IsInfinity(result))
                throw new InvalidDataException($"La formula del campo '{rule.Alias}' ha prodotto un valore non finito.");
            return result;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Formula non valida per il campo '{rule.Alias}': {ex.Message}", ex);
        }
    }

    private static double Aggregate(dynamic args, string name, Func<List<double>, double> aggregate)
    {
        var values = Arguments(args, name);
        if (values.Count == 0)
            throw new InvalidDataException($"{name} richiede almeno un valore.");
        return aggregate(values);
    }

    private static double One(dynamic args, string name)
    {
        var values = Exact(args, name, 1);
        return values[0];
    }

    private static List<double> Exact(dynamic args, string name, int count)
    {
        if (args.Count != count)
            throw new InvalidDataException($"{name} richiede esattamente {count} argomenti scalari.");
        var values = new List<double>();
        for (var index = 0; index < count; index++)
        {
            var rawValue = args.Evaluate(index);
            if (rawValue is IEnumerable && rawValue is not string)
                throw new InvalidDataException($"{name} richiede argomenti scalari, non range.");
            Flatten(rawValue, values, name);
        }
        return values;
    }

    private static List<double> Arguments(dynamic args, string name)
    {
        var values = new List<double>();
        for (var index = 0; index < args.Count; index++)
            Flatten(args.Evaluate(index), values, name);
        return values;
    }

    private static void Flatten(object? value, ICollection<double> target, string functionName)
    {
        if (value is null)
            throw new InvalidDataException($"{functionName} ha ricevuto un valore nullo.");
        if (value is IEnumerable enumerable && value is not string)
        {
            foreach (var item in enumerable)
                Flatten(item, target, functionName);
            return;
        }

        var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (double.IsNaN(number) || double.IsInfinity(number))
            throw new InvalidDataException($"{functionName} ha ricevuto un valore non finito.");
        target.Add(number);
    }

    private static double StandardDeviation(List<double> values)
    {
        var average = values.Average();
        return Math.Sqrt(values.Average(value => Math.Pow(value - average, 2)));
    }

    private static IReadOnlyList<string> TopologicalOrder(
        IReadOnlyDictionary<string, List<string>> dependencies)
    {
        var result = new List<string>();
        var states = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();

        void Visit(string alias)
        {
            if (states.TryGetValue(alias, out var state))
            {
                if (state == 1)
                {
                    var chain = stack.Reverse().SkipWhile(value => !string.Equals(value, alias, StringComparison.OrdinalIgnoreCase))
                        .Append(alias);
                    throw new InvalidDataException("Dipendenza ciclica nei campi calcolati: " + string.Join(" -> ", chain));
                }
                return;
            }

            states[alias] = 1;
            stack.Push(alias);
            foreach (var dependency in dependencies[alias])
                Visit(dependency);
            stack.Pop();
            states[alias] = 2;
            result.Add(alias);
        }

        foreach (var alias in dependencies.Keys)
            Visit(alias);
        return result;
    }

    private static IEnumerable<string> ExtractIdentifiers(string formula)
    {
        var functionPositions = FunctionCall.Matches(formula)
            .Select(match => match.Groups["name"].Index)
            .ToHashSet();
        return Identifier.Matches(formula)
            .Where(match => !functionPositions.Contains(match.Index))
            .Select(match => match.Value);
    }

    private static string NormalizeFunctionNames(string formula) => FunctionCall.Replace(formula, match =>
    {
        var name = match.Groups["name"].Value;
        var canonical = FunctionNames.FirstOrDefault(candidate =>
            string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
        return canonical is null ? match.Value : canonical + "(";
    });

    private static bool TryParseDouble(string? rawValue, CultureInfo culture, out double value) =>
        double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, culture, out value)
        || double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

    private static bool IsValidIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) && Identifier.Match(value).Value == value;

    private static string BaseDataType(FieldDataType dataType) => dataType == FieldDataType.Int ? "int" : "double";

    private sealed class SourceValues
    {
        public Dictionary<string, double> Scalars { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<double>> Ranges { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> InvalidNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
