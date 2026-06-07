using RRDA.Core.Validator;
using System.Text.Json;
using Xunit;

namespace RRDA.Core.Tests;

public sealed class FieldValidationErrorTests
{
    [Fact]
    public void JsonRoundTrip_PreservesPublicProperties()
    {
        var error = new FieldValidationError
        {
            Field = "Serial",
            Message = "Valore mancante.",
            Severity = "Error"
        };

        var json = JsonSerializer.Serialize(error);
        var restored = JsonSerializer.Deserialize<FieldValidationError>(json);

        Assert.NotNull(restored);
        Assert.Equal(error.Field, restored.Field);
        Assert.Equal(error.Message, restored.Message);
        Assert.Equal(error.Severity, restored.Severity);
    }
}
