using Vocab_LearningApp.Models.Domain;
using Vocab_LearningApp.Models.Requests;

namespace Vocab_LearningApp.Models.ViewModels;

public sealed class LoginPageViewModel
{
    public LoginInputModel Login { get; set; } = new();
    public RegisterInputModel Register { get; set; } = new();
    public string ActiveTab { get; set; } = "login";
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
}

public sealed class DashboardPageViewModel
{
    public required AuthenticatedUser User { get; init; }
    public required DashboardSummary Dashboard { get; init; }
}

public sealed class VocabularyPageViewModel
{
    public required AuthenticatedUser User { get; init; }
    public required IReadOnlyList<DeckSummary> Decks { get; init; }
    public string? Search { get; init; }
    public string? Tag { get; init; }
    public string? Sort { get; init; }
}

public sealed class DeckDetailPageViewModel
{
    public required AuthenticatedUser User { get; init; }
    public required DeckDetail Deck { get; init; }
    public string? Search { get; init; }
    public string? Status { get; init; }
    public string? Sort { get; init; }
}

public sealed class LearningPageViewModel
{
    public required AuthenticatedUser User { get; init; }
    public required LearningSession Session { get; init; }
}

public sealed class ProgressPageViewModel
{
    public required AuthenticatedUser User { get; init; }
    public required ProgressOverview Progress { get; init; }
}
