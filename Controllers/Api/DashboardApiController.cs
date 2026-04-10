using Microsoft.AspNetCore.Mvc;
using Vocab_LearningApp.Services;

namespace Vocab_LearningApp.Controllers.Api;

[Route("api/dashboard")]
public sealed class DashboardApiController : ApiControllerBase
{
    private readonly DashboardService _dashboardService;

    public DashboardApiController(DashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var dashboard = await _dashboardService.GetDashboardAsync(CurrentUserId, cancellationToken);
        return Ok(dashboard);
    }
}
