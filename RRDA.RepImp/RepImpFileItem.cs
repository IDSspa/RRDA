using RRDA.Data;

namespace RRDA.RepImp;

public sealed record RepImpFileItem(
    string Name,
    long Length,
    DateTime LastWriteTime,
    string Tipo,
    string FullPath,
    bool HasValidator,
    string? ValidatorPath)
{
    public string ValidatorStatus => string.IsNullOrWhiteSpace(Tipo)
        ? "Non applicabile"
        : HasValidator ? "Presente" : "Mancante";

    public ImportFileItem ToDataFileItem() =>
        new(Name, Length, LastWriteTime, Tipo, FullPath);
}
