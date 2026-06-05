using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RRDA.Core;
using RRDA.Data;
using RRDA.Plugins.Common;
using RRDA.Web.Security;
using RRDA.Web.Services;

namespace RRDA.Web.Areas.Plugins.Controllers;

[Area("Plugins")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class CatalogController(
    IPluginCatalog pluginCatalog,
    RRDADbContext db,
    IWebPluginManagementService managementService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var catalog = pluginCatalog.Current;
        var reportTypeKeys = await db.ReportTypes
            .AsNoTracking()
            .Select(type => type.Key)
            .ToListAsync();
        var reportTypeKeySet = reportTypeKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pluginNameSet = catalog.Plugins
            .Select(plugin => plugin.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return View(new PluginCatalogViewModel
        {
            Folder = catalog.Folder,
            LoadedAtUtc = catalog.LoadedAtUtc,
            Plugins = catalog.Plugins
                .OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
                .Select(plugin => new PluginCatalogItem
                {
                    Name = plugin.Name,
                    Version = plugin.Version,
                    SubjectKind = plugin.SubjectKind,
                    AssemblyFile = Path.GetFileName(plugin.GetType().Assembly.Location),
                    IsRegistered = reportTypeKeySet.Contains(plugin.Name)
                })
                .ToList(),
            Errors = catalog.Errors.ToList(),
            ReportTypesWithoutPlugin = reportTypeKeys
                .Where(key => !pluginNameSet.Contains(key))
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reload(CancellationToken cancellationToken)
    {
        var result = await managementService.ReloadAndSynchronizeAsync(cancellationToken);

        TempData[result.Catalog.Errors.Count == 0 ? "Success" : "Warning"] =
            $"Catalogo ricaricato: {result.Catalog.Plugins.Count} plugin, "
            + $"{result.Catalog.Errors.Count} errori, "
            + $"{result.Synchronization.Inserted.Count} tipi inseriti, "
            + $"{result.Synchronization.Updated.Count} aggiornati.";

        return RedirectToAction(nameof(Index));
    }
}

public sealed class PluginCatalogViewModel
{
    public string Folder { get; init; } = string.Empty;
    public DateTime LoadedAtUtc { get; init; }
    public List<PluginCatalogItem> Plugins { get; init; } = [];
    public List<PluginLoadError> Errors { get; init; } = [];
    public List<string> ReportTypesWithoutPlugin { get; init; } = [];
}

public sealed class PluginCatalogItem
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public ReportSubjectKind SubjectKind { get; init; }
    public string AssemblyFile { get; init; } = string.Empty;
    public bool IsRegistered { get; init; }
}
