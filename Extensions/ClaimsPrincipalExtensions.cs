using System.Security.Claims;

namespace Vocab_LearningApp.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static long GetRequiredUserId(this ClaimsPrincipal principal)
    {
        var rawId = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!long.TryParse(rawId, out var userId))
        {
            throw new InvalidOperationException("Authenticated user id is missing.");
        }

        return userId;
    }
}
