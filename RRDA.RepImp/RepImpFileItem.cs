using static RRDA.Data.ImportResultRepository;

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

    public FileItem ToDataFileItem() =>
        new(Name, Length, LastWriteTime, Tipo, FullPath);
}
