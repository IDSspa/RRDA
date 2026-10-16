using RRDA.Core;
using RRDA.Core.Validator;
using Xunit;

namespace RRDA.Core.Tests;

public sealed class CalculatedFieldEvaluatorTests
{
    [Fact]
    public void AppendCalculatedEntities_EvaluatesScalarsRangesAndDependencies()
    {
        var entities = new List<ReportEntityDto>
        {
            Scalar("pathLossMax", "12.5"),
            Scalar("pathLossMin", "8.0"),
            Range("RX_RO_LOSS", "2.0", 0),
            Range("RX_RO_LOSS", "4.0", 1)
        };
        var config = new ValidationConfig
        {
            CalculatedFields =
            [
                new() { Alias = "pathLossSpan", Formula = "pathLossMax - pathLossMin" },
                new() { Alias = "average", Formula = "Avg(RX_RO_LOSS)" },
                new() { Alias = "worst", Formula = "Max(pathLossSpan, average, 3)" },
                new() { Alias = "linear", Formula = "Pow(10, Log(10, 100))" }
            ]
        };

        CalculatedFieldEvaluator.AppendCalculatedEntities(entities, config);

        Assert.Equal("4.5", ValueOf(entities, "pathLossSpan"));
        Assert.Equal("3", ValueOf(entities, "average"));
        Assert.Equal("4.5", ValueOf(entities, "worst"));
        Assert.Equal("100", ValueOf(entities, "linear"));
        Assert.All(entities.Skip(4), entity => Assert.Equal("Calculated", entity.EntityKind));
    }

    [Fact]
    public void AppendCalculatedEntities_RejectsMissingInputsAndCycles()
    {
        var missing = new ValidationConfig
        {
            CalculatedFields = [new() { Alias = "result", Formula = "missingValue + 1" }]
        };
        var missingError = Assert.Throws<InvalidDataException>(() =>
            CalculatedFieldEvaluator.AppendCalculatedEntities([], missing));
        Assert.Contains("missingValue", missingError.Message);

        var cyclic = new ValidationConfig
        {
            CalculatedFields =
            [
                new() { Alias = "one", Formula = "two + 1" },
                new() { Alias = "two", Formula = "one + 1" }
            ]
        };
        var cycleError = Assert.Throws<InvalidDataException>(() =>
            CalculatedFieldEvaluator.AppendCalculatedEntities([], cyclic));
        Assert.Contains("ciclica", cycleError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppendCalculatedEntities_RejectsRangeAsScalar()
    {
        var entities = new List<ReportEntityDto> { Range("range", "2", 0) };
        var config = new ValidationConfig
        {
            CalculatedFields = [new() { Alias = "bad", Formula = "range * 2" }]
        };

        Assert.Throws<InvalidDataException>(() =>
            CalculatedFieldEvaluator.AppendCalculatedEntities(entities, config));
    }

    [Fact]
    public void AppendCalculatedEntities_FieldRulesTakePrecedenceOverSameCalculatedAlias()
    {
        var entities = new List<ReportEntityDto> { Scalar("pathLossSpan", "4.5") };
        var config = new ValidationConfig
        {
            FieldRules = [new FieldRule { DefinedName = "pathLossSpan" }],
            CalculatedFields =
            [
                new()
                {
                    Alias = "pathLossSpan",
                    Formula = "missingInput + 1"
                }
            ]
        };

        CalculatedFieldEvaluator.AppendCalculatedEntities(entities, config);

        Assert.Single(entities);
        Assert.Equal("4.5", ValueOf(entities, "pathLossSpan"));
    }

    private static ReportEntityDto Scalar(string name, string value) => new()
    {
        EntityKind = "Sheet1",
        Key = name,
        Properties = new Dictionary<string, string>
        {
            ["name"] = name,
            ["value"] = value,
            ["row_index"] = "-1",
            ["col_index"] = "-1"
        }
    };

    private static ReportEntityDto Range(string name, string value, int row) => new()
    {
        EntityKind = "Sheet1",
        Key = $"{name}[{row},0]",
        Properties = new Dictionary<string, string>
        {
            ["name"] = name,
            ["value"] = value,
            ["row_index"] = row.ToString(),
            ["col_index"] = "0"
        }
    };

    private static string ValueOf(IEnumerable<ReportEntityDto> entities, string key) =>
        entities.Single(entity => entity.Key == key).Properties["value"];
}
