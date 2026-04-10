using Microsoft.AspNetCore.Mvc;
using Vocab_LearningApp.Models.Requests;
using Vocab_LearningApp.Services;

namespace Vocab_LearningApp.Controllers.Api;

[Route("api/decks")]
public sealed class DecksApiController : ApiControllerBase
{
    private readonly DeckService _deckService;

    public DecksApiController(DeckService deckService)
    {
        _deckService = deckService;
    }

    [HttpGet]
    public async Task<IActionResult> GetDecks(
        [FromQuery] string? search,
        [FromQuery] string? tag,
        [FromQuery] string? sort,
        CancellationToken cancellationToken)
    {
        var decks = await _deckService.GetDecksAsync(CurrentUserId, search, tag, sort, cancellationToken);
        return Ok(decks);
    }

    [HttpGet("{deckId:long}")]
    public async Task<IActionResult> GetDeck(
        long deckId,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        CancellationToken cancellationToken = default)
    {
        var deck = await _deckService.GetDeckDetailAsync(CurrentUserId, deckId, search, status, sort, page, pageSize, cancellationToken);
        return deck is null ? NotFound() : Ok(deck);
    }

    [HttpPost]
    public async Task<IActionResult> CreateDeck([FromBody] CreateDeckRequest request, CancellationToken cancellationToken)
    {
        var deckId = await _deckService.CreateDeckAsync(CurrentUserId, request, cancellationToken);
        return Ok(new { id = deckId, message = "Tạo bộ từ thành công." });
    }

    [HttpPut("{deckId:long}")]
    public async Task<IActionResult> UpdateDeck(long deckId, [FromBody] UpdateDeckRequest request, CancellationToken cancellationToken)
    {
        var updated = await _deckService.UpdateDeckAsync(CurrentUserId, deckId, request, cancellationToken);
        return updated ? Ok(new { message = "Cập nhật bộ từ thành công." }) : NotFound();
    }

    [HttpDelete("{deckId:long}")]
    public async Task<IActionResult> DeleteDeck(long deckId, CancellationToken cancellationToken)
    {
        var deleted = await _deckService.DeleteDeckAsync(CurrentUserId, deckId, cancellationToken);
        return deleted ? Ok(new { message = "Đã xóa bộ từ." }) : NotFound();
    }

    [HttpPost("{deckId:long}/vocabularies")]
    public async Task<IActionResult> CreateVocabulary(long deckId, [FromBody] CreateVocabularyRequest request, CancellationToken cancellationToken)
    {
        var vocabularyId = await _deckService.CreateVocabularyAsync(CurrentUserId, deckId, request, cancellationToken);
        return vocabularyId.HasValue
            ? Ok(new { id = vocabularyId.Value, message = "Đã thêm từ vựng." })
            : NotFound();
    }

    [HttpPut("vocabularies/{vocabularyId:long}")]
    public async Task<IActionResult> UpdateVocabulary(long vocabularyId, [FromBody] UpdateVocabularyRequest request, CancellationToken cancellationToken)
    {
        var updated = await _deckService.UpdateVocabularyAsync(CurrentUserId, vocabularyId, request, cancellationToken);
        return updated ? Ok(new { message = "Đã cập nhật từ vựng." }) : NotFound();
    }

    [HttpDelete("vocabularies/{vocabularyId:long}")]
    public async Task<IActionResult> DeleteVocabulary(long vocabularyId, CancellationToken cancellationToken)
    {
        var deleted = await _deckService.DeleteVocabularyAsync(CurrentUserId, vocabularyId, cancellationToken);
        return deleted ? Ok(new { message = "Đã xóa từ vựng." }) : NotFound();
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import(
        [FromForm] DeckImportRequest request,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var deckId = await _deckService.ImportDeckAsync(CurrentUserId, request, file, cancellationToken);
        return deckId.HasValue
            ? Ok(new { id = deckId.Value, message = "Import bộ từ thành công." })
            : BadRequest(new { message = "Không thể import file." });
    }

    [HttpGet("{deckId:long}/export")]
    public async Task<IActionResult> Export(long deckId, [FromQuery] string format = "csv", CancellationToken cancellationToken = default)
    {
        var export = await _deckService.ExportDeckAsync(CurrentUserId, deckId, format, cancellationToken);
        return export.HasValue
            ? File(export.Value.Content, export.Value.ContentType, export.Value.FileName)
            : NotFound();
    }
}
