using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Linq;
using Vocab_LearningApp.Extensions;
using Vocab_LearningApp.Models;
using Vocab_LearningApp.Models.Domain;
using Vocab_LearningApp.Models.Requests;
using Vocab_LearningApp.Models.ViewModels;
using Vocab_LearningApp.Services;

namespace Vocab_LearningApp.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly AuthService _authService;
    private readonly DashboardService _dashboardService;
    private readonly DeckService _deckService;
    private readonly LearningService _learningService;
    private readonly ProgressService _progressService;
    private readonly AccountService _accountService;
    private readonly JwtTokenService _jwtTokenService;

    public HomeController(
        ILogger<HomeController> logger,
        AuthService authService,
        DashboardService dashboardService,
        DeckService deckService,
        LearningService learningService,
        ProgressService progressService,
        AccountService accountService,
        JwtTokenService jwtTokenService
        )
    {
        _logger = logger;
        _authService = authService;
        _dashboardService = dashboardService;
        _deckService = deckService;
        _learningService = learningService;
        _progressService = progressService;
        _accountService = accountService;
        _jwtTokenService = jwtTokenService;
    }

    [AllowAnonymous]
    public IActionResult Index()
    {
        return User.Identity?.IsAuthenticated == true
            ? RedirectToAction(nameof(Dashboard))
            : RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(nameof(Dashboard));
        }

        return View(new LoginPageViewModel());
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginInputModel input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(new LoginPageViewModel
            {
                Login = input,
                ActiveTab = "login",
                ErrorMessage = BuildValidationMessage("Vui lòng kiểm tra lại thông tin đăng nhập.")
            });
        }

        var authResult = await _authService.LoginAsync(input, cancellationToken);
        if (!authResult.Succeeded || authResult.AccessToken is null)
        {
            return View(new LoginPageViewModel
            {
                Login = input,
                ActiveTab = "login",
                ErrorMessage = authResult.ErrorMessage ?? "Đăng nhập không thành công."
            });
        }

        AppendAccessTokenCookie(authResult.AccessToken, authResult.ExpiresAtUtc);
        return RedirectToAction(nameof(Dashboard));
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterInputModel input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Login", new LoginPageViewModel
            {
                Register = input,
                ActiveTab = "register",
                ErrorMessage = BuildValidationMessage("Vui lòng kiểm tra lại thông tin đăng ký.")
            });
        }

        var authResult = await _authService.RegisterAsync(input, cancellationToken);
        if (!authResult.Succeeded || authResult.AccessToken is null)
        {
            return View("Login", new LoginPageViewModel
            {
                Register = input,
                ActiveTab = "register",
                ErrorMessage = authResult.ErrorMessage ?? "Đăng ký không thành công."
            });
        }

        AppendAccessTokenCookie(authResult.AccessToken, authResult.ExpiresAtUtc);
        return RedirectToAction(nameof(Dashboard));
    }

    [Authorize]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var dashboard = await _dashboardService.GetDashboardAsync(user.Id, cancellationToken);
        return View("Dashboard/Dashboard", new DashboardPageViewModel
        {
            User = user,
            Dashboard = dashboard
        });
    }

    [Authorize]
    public async Task<IActionResult> Vocabulary(
        string? search,
        string? tag,
        string? sort,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var decks = await _deckService.GetDecksAsync(user.Id, search, tag, sort, cancellationToken);
        return View("Dashboard/Vocabulary", new VocabularyPageViewModel
        {
            User = user,
            Decks = decks,
            Search = search,
            Tag = tag,
            Sort = sort
        });
    }

    [Authorize]
    public async Task<IActionResult> Progress(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var progress = await _progressService.GetProgressAsync(user.Id, cancellationToken);
        return View("Dashboard/Progress", new ProgressPageViewModel
        {
            User = user,
            Progress = progress
        });
    }

    [Authorize]
    public async Task<IActionResult> Vocab_detail(
        long? deckId,
        string? search,
        string? status,
        string? sort,
        int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        if (!deckId.HasValue)
        {
            return RedirectToAction(nameof(Vocabulary));
        }

        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var deck = await _deckService.GetDeckDetailAsync(user.Id, deckId.Value, search, status, sort, pageNumber, 12, cancellationToken);
        if (deck is null)
        {
            return RedirectToAction(nameof(Vocabulary));
        }

        return View("Vocab_detail", new DeckDetailPageViewModel
        {
            User = user,
            Deck = deck,
            Search = search,
            Status = status,
            Sort = sort
        });
    }

    [Authorize]
    public async Task<IActionResult> Learning(long? deckId, bool practice = false, CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var session = await _learningService.GetLearningSessionAsync(user.Id, deckId, practice, cancellationToken);
        return View("Learning", new LearningPageViewModel
        {
            User = user,
            Session = session
        });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("access_token");
        return RedirectToAction(nameof(Login));
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    private async Task<AuthenticatedUser?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _authService.GetUserAsync(User.GetRequiredUserId(), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Unable to resolve current authenticated user.");
            Response.Cookies.Delete("access_token");
            return null;
        }
    }

    private void AppendAccessTokenCookie(string accessToken, DateTime expiresAtUtc)
    {
        Response.Cookies.Append("access_token", accessToken, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Expires = new DateTimeOffset(expiresAtUtc)
        });
    }

    private string BuildValidationMessage(string fallbackMessage)
    {
        var messages = ModelState.Values
            .SelectMany(x => x.Errors)
            .Select(x => x.ErrorMessage)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToArray();

        return messages.Length > 0
            ? string.Join(" ", messages)
            : fallbackMessage;
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditProfile([FromForm] EditProfileRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                message = BuildValidationMessage("Dữ liệu cập nhật không hợp lệ.")
            });
        }

        var result = await _accountService.UpdateProfileAsync(
            User.GetRequiredUserId(),
            request,
            cancellationToken);

        if (!result.Succeeded || result.UpdatedUser is null)
        {
            return BadRequest(new { message = result.Message });
        }

        var (token, expiresAtUtc) = _jwtTokenService.CreateToken(result.UpdatedUser);
        Response.Cookies.Append("access_token", token, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Expires = new DateTimeOffset(expiresAtUtc)
        });

        return Ok(new { message = result.Message });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword([FromForm] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                message = BuildValidationMessage("Dữ liệu đổi mật khẩu không hợp lệ.")
            });
        }

        var result = await _accountService.ChangePasswordAsync(
            User.GetRequiredUserId(),
            request,
            cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { message = result.Message });
        }

        return Ok(new { message = result.Message });
    }
}