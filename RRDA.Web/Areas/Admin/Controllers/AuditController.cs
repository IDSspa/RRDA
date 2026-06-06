using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RRDA.Data;
using RRDA.Web.Security;

namespace RRDA.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class AuditController(RRDADbContext db) : Controller
{
    public async Task<IActionResult> Index(
        string? application, string? operation, string? result, string? userName,
        DateTime? from, DateTime? to, int page = 1, int pageSize = 50)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 25, 100);
        var query = db.AuditEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(application))
            query = query.Where(e => e.Application == application);
        if (!string.IsNullOrWhiteSpace(operation))
            query = query.Where(e => e.Operation.Contains(operation));
        if (!string.IsNullOrWhiteSpace(result))
            query = query.Where(e => e.Result == result);
        if (!string.IsNullOrWhiteSpace(userName))
            query = query.Where(e => e.UserName != null && e.UserName.Contains(userName));
        if (from.HasValue)
            query = query.Where(e => e.OccurredAtUtc >= from.Value.ToUniversalTime());
        if (to.HasValue)
            query = query.Where(e => e.OccurredAtUtc < to.Value.AddDays(1).ToUniversalTime());

        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, totalPages);

        return View(new AuditIndexViewModel
        {
            Events = await query.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(),
            Applications = await db.AuditEvents.AsNoTracking().Select(e => e.Application)
                .Distinct().OrderBy(value => value).ToListAsync(),
            Results = await db.AuditEvents.AsNoTracking().Select(e => e.Result)
                .Distinct().OrderBy(value => value).ToListAsync(),
            Application = application,
            Operation = operation,
            Result = result,
            UserName = userName,
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages
        });
    }
}

public sealed class AuditIndexViewModel
{
    public List<AuditEvent> Events { get; init; } = [];
    public List<string> Applications { get; init; } = [];
    public List<string> Results { get; init; } = [];
    public string? Application { get; init; }
    public string? Operation { get; init; }
    public string? Result { get; init; }
    public string? UserName { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
}
