using Pressing.Core.Dtos;
using Pressing.Core.Options;

namespace Pressing.Client;

/// <summary>Réponse de GET /api/public/info.</summary>
public record PublicInfoDto(
    ShopInfo Shop,
    IReadOnlyList<CategoryDto> Categories,
    IReadOnlyList<PromotionDto> Promotions,
    IReadOnlyList<string> Features);

/// <summary>Réponse de GET /api/auth/me. <c>Type</c> vaut « staff » ou « client ».</summary>
public record MeDto(string Type, StaffUserDto? Staff, ClientDto? Client);
