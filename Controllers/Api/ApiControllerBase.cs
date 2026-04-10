using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vocab_LearningApp.Extensions;

namespace Vocab_LearningApp.Controllers.Api;

[ApiController]
[Authorize]
public abstract class ApiControllerBase : ControllerBase
{
    protected long CurrentUserId => User.GetRequiredUserId();

    protected void AppendAccessTokenCookie(string accessToken, DateTime expiresAtUtc)
    {
        Response.Cookies.Append("access_token", accessToken, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Path = "/",
            Expires = new DateTimeOffset(expiresAtUtc)
        });
    }

    protected void DeleteAccessTokenCookie()
    {
        Response.Cookies.Delete("access_token", new CookieOptions
        {
            Path = "/"
        });
    }
}