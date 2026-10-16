namespace RRDA.Core.Validator;

/// <summary>Definisce un valore numerico materializzato durante l'importazione.</summary>
public sealed class CalculatedFieldRule
{
    public string Alias { get; set; } = string.Empty;
    public string Formula { get; set; } = string.Empty;
    public FieldDataType DataType { get; set; } = FieldDataType.Double;
    public string? Unit { get; set; }
}
