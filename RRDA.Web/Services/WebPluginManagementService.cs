using RRDA.Data;
using RRDA.Plugins.Common;
using Microsoft.EntityFrameworkCore;

namespace RRDA.Web.Services;

public interface IWebPluginManagementService
{
    Task<WebPluginRefreshResult> ReloadAndSynchronizeAsync(
        CancellationToken cancellationToken = default);
}

public sealed record WebPluginRefreshResult(
    PluginCatalogSnapshot Catalog,
    ReportTypeSyncResult Synchronization);

public sealed class WebPluginManagementService(
    IPluginCatalog pluginCatalog,
    IDbContextFactory<RRDADbContext> dbFactory,
    IReportTypeSynchronizer reportTypeSynchronizer,
    IConfiguration configuration,
    IWebHostEnvironment environment) : IWebPluginManagementService
{
    public async Task<WebPluginRefreshResult> ReloadAndSynchronizeAsync(
        CancellationToken cancellationToken = default)
    {
        var catalog = pluginCatalog.Reload(
            configuration.GetValue<string>("Plugins:Folder"),
            environment.ContentRootPath);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var synchronization = await reportTypeSynchronizer.SyncAsync(
            db,
            catalog.Plugins,
            cancellationToken);

        return new WebPluginRefreshResult(catalog, synchronization);
    }
}
