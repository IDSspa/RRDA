using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace RRDA.Web.Areas.Data.Models;

public sealed class BatchIndexViewModel
{
    public required IReadOnlyList<BatchListItemViewModel> Batches { get; init; }
    public bool CanManage { get; init; }
}

public sealed class BatchListItemViewModel
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public bool IsMaintenance { get; init; }
    public int ReportCount { get; init; }
}

public sealed class BatchCreateViewModel
{
    [Required(ErrorMessage = "Il nome del batch è obbligatorio.")]
    [StringLength(200, ErrorMessage = "Il nome non può superare 200 caratteri.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "La descrizione non può superare 2000 caratteri.")]
    public string? Description { get; set; }

    public bool IsMaintenance { get; set; }
}

public sealed class BatchDeleteViewModel
{
    public int Id { get; set; }

    [ValidateNever]
    public string Name { get; set; } = string.Empty;

    [ValidateNever]
    public int ReportCount { get; set; }
    public BatchDeleteStrategy Strategy { get; set; } = BatchDeleteStrategy.RemoveAssociation;
}

public enum BatchDeleteStrategy
{
    RemoveAssociation = 0,
    DeleteReports = 1
}
