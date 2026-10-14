using System.Security.Claims;
using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IAtlasService
{
    Task<bool> HasAccessAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AtlasPlan>> ListPlansAsync(
        DateTime fromDate,
        DateTime toDate,
        string currentUserName,
        CancellationToken cancellationToken = default);

    Task<int> CreatePlanAsync(
        AtlasCreatePlan plan,
        string actorUserName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AtlasUser>> ListUsersAsync(
        string currentUserName,
        CancellationToken cancellationToken = default);

    Task UpdateUserColorAsync(
        string actorUserName,
        string color,
        CancellationToken cancellationToken = default);

    Task DeletePlanAsync(
        int planId,
        string actorUserName,
        CancellationToken cancellationToken = default);

    IReadOnlyList<AtlasHoliday> GetPublicHolidays(int year);
}
