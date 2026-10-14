using System.Security.Claims;

namespace DATA_EXCHANGE_HUB.Services;

public interface IUserNameResolver
{
    string ResolveUserName(ClaimsPrincipal user);
}
