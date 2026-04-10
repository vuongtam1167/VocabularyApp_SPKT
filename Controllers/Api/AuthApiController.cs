using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vocab_LearningApp.Models.Requests;
using Vocab_LearningApp.Services;

namespace Vocab_LearningApp.Controllers.Api;

[Route("api/auth")]
public sealed class AuthApiController : ApiControllerBase
{
    private readonly AuthService _authService;

    public AuthApiController(AuthService authService)
    {
        _authService = authService;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginInputModel request, CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(request, cancellationToken);
        if (!result.Succeeded || result.AccessToken is null || result.User is null)
        {
            return BadRequest(new { message = result.ErrorMessage });
        }

        AppendAccessTokenCookie(result.AccessToken, result.ExpiresAtUtc);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterInputModel request, CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterAsync(request, cancellationToken);
        if (!result.Succeeded || result.AccessToken is null || result.User is null)
        {
            return BadRequest(new { message = result.ErrorMessage });
        }

        AppendAccessTokenCookie(result.AccessToken, result.ExpiresAtUtc);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.GoogleLoginAsync(request, cancellationToken);
        if (!result.Succeeded || result.AccessToken is null || result.User is null)
        {
            return BadRequest(new { message = result.ErrorMessage });
        }

        AppendAccessTokenCookie(result.AccessToken, result.ExpiresAtUtc);
        return Ok(result);
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var user = await _authService.GetUserAsync(CurrentUserId, cancellationToken);
        return user is null ? Unauthorized() : Ok(user);
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("access_token");
        return Ok(new { message = "Đã đăng xuất." });
    }
}
