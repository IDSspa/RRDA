namespace RRDA.RepImp
{
    public partial class MainWindow
    {
        // DTO per la ListView dei file (include il tipo determinato dal plugin)
        private sealed record FileItem(string Name, long Length, DateTime LastWriteTime, string Tipo, string FullPath);
    }
}