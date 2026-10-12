using Microsoft.EntityFrameworkCore;
using Nms.Api.Contracts;
using Nms.Api.Data;
using Nms.Api.Domain;
using Nms.Api.Monitoring;

namespace Nms.Api.Endpoints;

public static class TopologyEndpoints
{
    public static void MapTopologyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/topology").WithTags("Topology").RequireAuthorization();

        group.MapGet("/", async (TopologyService topology, string? site, CancellationToken ct) =>
            Results.Ok(await topology.GetGraphAsync(string.IsNullOrWhiteSpace(site) ? null : site, ct)));

        group.MapGet("/path", async (TopologyService topology, Guid from, Guid to, CancellationToken ct) =>
            Results.Ok(await topology.FindPathAsync(from, to, ct)));

        group.MapPost("/links", async (SaveLinkRequest request, NmsDbContext db, CancellationToken ct) =>
        {
            if (request.ADeviceId == request.BDeviceId)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["bDeviceId"] = ["A link needs two different devices."]
                });
            }

            var deviceCount = await db.Devices.CountAsync(d => d.Id == request.ADeviceId || d.Id == request.BDeviceId, ct);
            if (deviceCount != 2)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["aDeviceId"] = ["Both devices must exist."]
                });
            }

            var exists = await db.Links.AnyAsync(l =>
                (l.ADeviceId == request.ADeviceId && l.BDeviceId == request.BDeviceId)
                || (l.ADeviceId == request.BDeviceId && l.BDeviceId == request.ADeviceId), ct);

            if (exists)
            {
                return Results.Conflict(new { message = "These devices are already linked." });
            }

            var link = new Link
            {
                Id = Guid.NewGuid(),
                ADeviceId = request.ADeviceId,
                AInterfaceId = request.AInterfaceId,
                BDeviceId = request.BDeviceId,
                BInterfaceId = request.BInterfaceId,
                CapacityMbps = request.CapacityMbps,
                LinkType = request.LinkType,
                Status = LinkStatus.Unknown,
                CreatedAt = DateTimeOffset.UtcNow
            };

            db.Links.Add(link);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/topology/links/{link.Id}", new { link.Id });
        }).RequireAuthorization("ManageInventory");

        group.MapDelete("/links/{id:guid}", async (Guid id, NmsDbContext db, CancellationToken ct) =>
        {
            var removed = await db.Links.Where(l => l.Id == id).ExecuteDeleteAsync(ct);
            return removed == 0 ? Results.NotFound() : Results.NoContent();
        }).RequireAuthorization("ManageInventory");
    }
}
