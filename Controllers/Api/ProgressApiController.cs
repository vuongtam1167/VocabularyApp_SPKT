using Microsoft.AspNetCore.Mvc;
using Vocab_LearningApp.Services;

namespace Vocab_LearningApp.Controllers.Api;

[Route("api/progress")]
public sealed class ProgressApiController : ApiControllerBase
{
    private readonly ProgressService _progressService;

    public ProgressApiController(ProgressService progressService)
    {
        _progressService = progressService;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var progress = await _progressService.GetProgressAsync(CurrentUserId, cancellationToken);
        return Ok(progress);
    }
}
