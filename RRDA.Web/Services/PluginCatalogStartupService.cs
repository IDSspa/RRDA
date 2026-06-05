namespace RRDA.Web.Services;

public sealed class PluginCatalogStartupService(
    IServiceScopeFactory scopeFactory,
    ILogger<PluginCatalogStartupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var managementService = scope.ServiceProvider
                .GetRequiredService<IWebPluginManagementService>();
            var result = await managementService.ReloadAndSynchronizeAsync(stoppingToken);

            logger.LogInformation(
                "Catalogo plugin caricato da {PluginFolder}: {PluginCount} plugin, {ErrorCount} errori, {InsertedCount} ReportTypes inseriti, {UpdatedCount} aggiornati.",
                result.Catalog.Folder,
                result.Catalog.Plugins.Count,
                result.Catalog.Errors.Count,
                result.Synchronization.Inserted.Count,
                result.Synchronization.Updated.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Errore durante il caricamento iniziale del catalogo plugin.");
        }
    }
}
