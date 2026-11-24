using Pressing.Core.Dtos;
using Pressing.Core.Security;
using Pressing.Core.Services;

namespace Pressing.Api.Endpoints;

public static class ClientEndpoints
{
    public static IEndpointRouteBuilder MapClientEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/clients").WithTags("Clients");

        group.MapGet("/", async (
            ClientService clients,
            string? search,
            int? page,
            int? pageSize,
            bool? includeInactive,
            CancellationToken ct) =>
            Results.Ok(await clients.SearchAsync(search, page ?? 1, pageSize ?? 25, includeInactive ?? false, ct)))
            .RequireAuthorization("StaffOnly");

        group.MapGet("/lookup", async (
            ClientService clients,
            string? search,
            int? take,
            CancellationToken ct) =>
            Results.Ok(await clients.SearchLightAsync(search, take ?? 20, ct)))
            .RequireAuthorization("StaffOnly");

        group.MapGet("/{id:int}", async (int id, ClientService clients, CancellationToken ct) =>
        {
            ClientDetailDto? detail = await clients.GetDetailAsync(id, ct);
            return detail is null
                ? Results.NotFound(new { message = "Client introuvable." })
                : Results.Ok(detail);
        }).RequireAuthorization("StaffOnly");

        group.MapPost("/", async (
            CreateClientRequest request,
            CurrentUser current,
            ClientService clients,
            CancellationToken ct) =>
        {
            ClientDto dto = await clients.CreateAsync(request, current, ct);
            return Results.Created($"/api/clients/{dto.Id}", dto);
        }).RequireAuthorization("StaffOnly");

        group.MapPut("/{id:int}", async (
            int id,
            UpdateClientRequest request,
            CurrentUser current,
            ClientService clients,
            CancellationToken ct) =>
            Results.Ok(await clients.UpdateAsync(id, request, current, ct)))
            .RequireAuthorization("StaffOnly");

        group.MapPost("/{id:int}/password", async (
            int id,
            SetClientPasswordRequest request,
            CurrentUser current,
            ClientService clients,
            CancellationToken ct) =>
            Results.Ok(await clients.SetPasswordAsync(id, request, current, ct)))
            .RequireAuthorization("StaffOnly");

        group.MapPost("/{id:int}/loyalty", async (
            int id,
            AdjustLoyaltyRequest request,
            CurrentUser current,
            ClientService clients,
            CancellationToken ct) =>
            Results.Ok(await clients.AdjustLoyaltyAsync(id, request.Points, request.Note, current, ct)))
            .RequireAuthorization("ManagerOrAdmin");

        return app;
    }
}
