using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace RRDA.Data.Tests;

public sealed class AuditServiceTests
{
    [Fact]
    public async Task WriteAsync_PersistsStructuredAuditEvent()
    {
        await using var db = CreateDbContext();
        IAuditService service = new AuditService();

        await service.WriteAsync(
            db,
            new AuditEventRequest(
                "RRDA.Web",
                "Report.ImportSucceeded",
                "Success",
                UserName: @"IDS\operator",
                EntityType: "ReportFile",
                EntityId: "42",
                Details: new { Plugin = "ALI", Entities = 10 },
                CorrelationId: "correlation-id",
                ClientIp: "127.0.0.1",
                MachineName: "WEB-SERVER"));

        var auditEvent = await db.AuditEvents.SingleAsync();
        Assert.Equal("RRDA.Web", auditEvent.Application);
        Assert.Equal("Report.ImportSucceeded", auditEvent.Operation);
        Assert.Equal("Success", auditEvent.Result);
        Assert.Equal("WEB-SERVER", auditEvent.MachineName);
        Assert.Equal("42", auditEvent.EntityId);

        using var details = JsonDocument.Parse(auditEvent.DetailsJson!);
        Assert.Equal("ALI", details.RootElement.GetProperty("plugin").GetString());
        Assert.Equal(10, details.RootElement.GetProperty("entities").GetInt32());
    }

    private static RRDADbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RRDADbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RRDADbContext(options);
    }
}
