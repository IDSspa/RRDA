using RRDA.Data;

namespace RRDA.RepImp;

public sealed record RepImpFileItem(
    string Name,
    long Size,
    DateTime LastWriteTime,
    string Type,
    string FullPath,
    bool HasValidator,
    string? ValidatorPath)
{
    public string ValidatorStatus => string.IsNullOrWhiteSpace(Type)
        ? "Non applicabile"
        : HasValidator ? "Presente" : "Mancante";

    public ImportFileItem ToDataFileItem() =>
        new(Name, Size, LastWriteTime, Type, FullPath);
}
