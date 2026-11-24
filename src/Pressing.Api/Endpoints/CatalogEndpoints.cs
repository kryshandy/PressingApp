using Pressing.Core.Dtos;
using Pressing.Core.Security;
using Pressing.Core.Services;

namespace Pressing.Api.Endpoints;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var catalog = app.MapGroup("/api/catalog").WithTags("Catalogue");

        catalog.MapGet("/", async (CatalogService service, bool? includeInactive, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(includeInactive ?? false, ct)))
            .RequireAuthorization("StaffOnly");

        catalog.MapPost("/", async (
            CreateCategoryRequest request,
            CurrentUser current,
            CatalogService service,
            CancellationToken ct) =>
        {
            CategoryDto dto = await service.CreateAsync(request, current, ct);
            return Results.Created($"/api/catalog/{dto.Id}", dto);
        }).RequireAuthorization("ManagerOrAdmin");

        catalog.MapPut("/{id:int}", async (
            int id,
            UpdateCategoryRequest request,
            CurrentUser current,
            CatalogService service,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, request, current, ct)))
            .RequireAuthorization("ManagerOrAdmin");

        var promotions = app.MapGroup("/api/promotions").WithTags("Promotions");

        promotions.MapGet("/", async (PromotionService service, bool? includeInactive, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(includeInactive ?? true, ct)))
            .RequireAuthorization("StaffOnly");

        promotions.MapPost("/", async (
            CreatePromotionRequest request,
            CurrentUser current,
            PromotionService service,
            CancellationToken ct) =>
        {
            PromotionDto dto = await service.CreateAsync(request, current, ct);
            return Results.Created($"/api/promotions/{dto.Id}", dto);
        }).RequireAuthorization("ManagerOrAdmin");

        promotions.MapPut("/{id:int}", async (
            int id,
            UpdatePromotionRequest request,
            CurrentUser current,
            PromotionService service,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, request, current, ct)))
            .RequireAuthorization("ManagerOrAdmin");

        promotions.MapPost("/validate", async (
            ValidatePromotionRequest request,
            PromotionService service,
            CancellationToken ct) =>
            Results.Ok(await service.ValidateAsync(request.Code, request.Subtotal, ct)))
            .RequireAuthorization("StaffOnly");

        return app;
    }
}
