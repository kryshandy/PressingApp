namespace Pressing.Core.Dtos;

public record CategoryPriceDto(
    int Id,
    ServiceType ServiceType,
    string ServiceLabel,
    decimal Price,
    decimal ExpressSupplement,
    int EstimatedDays,
    bool IsActive);

public record CategoryDto(
    int Id,
    string Name,
    string Icon,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<CategoryPriceDto> Prices);

public record CreateCategoryRequest(
    string Name,
    string Icon,
    int SortOrder,
    IReadOnlyList<UpdateCategoryPriceRequest> Prices);

public record UpdateCategoryRequest(
    string Name,
    string Icon,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<UpdateCategoryPriceRequest> Prices);

public record UpdateCategoryPriceRequest(
    ServiceType ServiceType,
    decimal Price,
    decimal ExpressSupplement,
    int EstimatedDays,
    bool IsActive);

public record PromotionDto(
    int Id,
    string Code,
    string Description,
    PromotionType Type,
    string TypeLabel,
    decimal Value,
    decimal MinOrderAmount,
    DateTime ValidFrom,
    DateTime ValidTo,
    int MaxUses,
    int UsedCount,
    bool IsActive);

public record CreatePromotionRequest(
    string Code,
    string Description,
    PromotionType Type,
    decimal Value,
    decimal MinOrderAmount,
    DateTime ValidFrom,
    DateTime ValidTo,
    int MaxUses,
    bool IsActive);

public record UpdatePromotionRequest(
    string Code,
    string Description,
    PromotionType Type,
    decimal Value,
    decimal MinOrderAmount,
    DateTime ValidFrom,
    DateTime ValidTo,
    int MaxUses,
    bool IsActive);

public record ValidatePromotionRequest(string Code, decimal Subtotal);

public record PromotionValidationDto(
    bool Valid,
    string Message,
    decimal Discount,
    PromotionDto? Promotion);
