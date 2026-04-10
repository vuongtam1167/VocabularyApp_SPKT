using Microsoft.AspNetCore.Mvc;
using Vocab_LearningApp.Models.Requests;
using Vocab_LearningApp.Services;

namespace Vocab_LearningApp.Controllers.Api;

[Route("api/learning")]
public sealed class LearningApiController : ApiControllerBase
{
    private readonly LearningService _learningService;

    public LearningApiController(LearningService learningService)
    {
        _learningService = learningService;
    }

    [HttpGet("session")]
    public async Task<IActionResult> GetSession([FromQuery] long? deckId, CancellationToken cancellationToken)
    {
        var session = await _learningService.GetLearningSessionAsync(CurrentUserId, deckId, cancellationToken);
        return Ok(session);
    }

    [HttpPost("review")]
    public async Task<IActionResult> Review([FromBody] ReviewRequest request, CancellationToken cancellationToken)
    {
        var result = await _learningService.SubmitReviewAsync(CurrentUserId, request, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
