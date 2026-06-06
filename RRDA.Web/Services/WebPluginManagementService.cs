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
    IWebAuditService auditService,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<WebPluginManagementService> logger) : IWebPluginManagementService
{
    public async Task<WebPluginRefreshResult> ReloadAndSynchronizeAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var catalog = pluginCatalog.Reload(
                configuration.GetValue<string>("Plugins:Folder"),
                environment.ContentRootPath);

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var synchronization = await reportTypeSynchronizer.SyncAsync(
                db,
                catalog.Plugins,
                cancellationToken);

            await auditService.WriteAsync(
                "Plugins.SynchronizationCompleted",
                "Success",
                entityType: "PluginCatalog",
                description: "Catalogo plugin ricaricato e ReportTypes sincronizzati.",
                details: new
                {
                    catalog.Folder,
                    PluginCount = catalog.Plugins.Count,
                    ErrorCount = catalog.Errors.Count,
                    synchronization.Inserted,
                    synchronization.Updated
                },
                cancellationToken: cancellationToken);

            return new WebPluginRefreshResult(catalog, synchronization);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Errore durante la sincronizzazione del catalogo plugin.");
            await auditService.WriteAsync(
                "Plugins.SynchronizationFailed",
                "Failed",
                entityType: "PluginCatalog",
                description: ex.GetBaseException().Message,
                cancellationToken: cancellationToken);
            throw;
        }
    }
}
