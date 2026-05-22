namespace RRDA.Core.Tabular
{
    /// <summary>
    /// Provider governato per trasformare entità di report in una vista tabellare orizzontale.
    /// </summary>
    public interface ITabularProjectionProvider
    {
        string ReportTypeKey { get; }
        TabularSchema GetSchema();
        Task<TabularResult> BuildAsync(TabularRequest request, CancellationToken ct = default);
    }
}
