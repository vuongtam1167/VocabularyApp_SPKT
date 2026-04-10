namespace Vocab_LearningApp.Models.Domain;

public enum ReviewRating
{
    Again = 0,
    Hard = 1,
    Good = 2,
    Easy = 3
}

public sealed record AuthenticatedUser(
    long Id,
    string FullName,
    string Email,
    string AuthProvider,
    string? AvatarUrl,
    string? LearningGoal,
    string? CurrentLevel,
    int DailyTarget);

public sealed record NotificationSummary(
    long Id,
    int Type,
    string Content,
    bool IsRead,
    DateTime? ScheduledAt,
    DateTime CreatedAt);

public sealed record DashboardStats(
    int DeckCount,
    int TotalVocabularyCount,
    int LearnedVocabularyCount,
    int MasteredVocabularyCount,
    int DueReviewCount,
    int CurrentStreak,
    int LongestStreak,
    int AccuracyPercentage,
    int StudiedTodayCount,
    int DailyTarget);

public sealed record DailyLearningPlan(
    int NewWordsTarget,
    int RemainingNewWords,
    int DueReviewWords);

public sealed record DeckSummary(
    long Id,
    string Title,
    string? Description,
    string? Tags,
    bool IsPublic,
    DateTime CreatedAt,
    int TotalWords,
    int LearnedWords,
    int MasteredWords,
    int DueWords)
{
    public string PrimaryTag =>
        string.IsNullOrWhiteSpace(Tags)
            ? "General"
            : Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "General";

    public int NewWords => Math.Max(TotalWords - LearnedWords, 0);

    public int ProgressPercentage =>
        TotalWords == 0 ? 0 : (int)Math.Round((double)LearnedWords / TotalWords * 100, MidpointRounding.AwayFromZero);
}

public sealed record DashboardSummary(
    DashboardStats Stats,
    DailyLearningPlan DailyPlan,
    IReadOnlyList<DeckSummary> RecentDecks,
    IReadOnlyList<NotificationSummary> Notifications);

public sealed record VocabularyItem(
    long Id,
    string Word,
    string? Pronunciation,
    int? PartOfSpeech,
    string? MeaningVi,
    string? DescriptionEn,
    string? ExampleSentence,
    string? Collocations,
    string? RelatedWords,
    string? Note,
    string? ImageUrl,
    string? AudioUrl,
    string Status,
    int Interval,
    double EaseFactor,
    int Repetitions,
    DateTime? NextReviewDate,
    DateTime CreatedAt)
{
    public bool IsDue => NextReviewDate.HasValue && NextReviewDate.Value.Date <= DateTime.Today;
}

public sealed record PaginationInfo(int Page, int PageSize, int TotalItems)
{
    public int TotalPages => PageSize == 0 ? 1 : (int)Math.Ceiling((double)TotalItems / PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}

public sealed record DeckDetail(
    long Id,
    string Title,
    string? Description,
    string? Tags,
    bool IsPublic,
    DateTime CreatedAt,
    int TotalWords,
    int LearnedWords,
    int DueWords,
    IReadOnlyList<VocabularyItem> VocabularyItems,
    PaginationInfo Pagination)
{
    public int ProgressPercentage =>
        TotalWords == 0 ? 0 : (int)Math.Round((double)LearnedWords / TotalWords * 100, MidpointRounding.AwayFromZero);
}

public sealed record ReviewOptionPreview(
    ReviewRating Rating,
    string Label,
    string Emoji,
    string ScheduleText);

public sealed record LearningCard(
    long VocabularyId,
    long DeckId,
    string DeckTitle,
    string Word,
    string? Pronunciation,
    string? MeaningVi,
    string? DescriptionEn,
    string? ExampleSentence,
    string? Collocations,
    string? RelatedWords,
    string? Note,
    string Status,
    int Interval,
    double EaseFactor,
    int Repetitions,
    bool IsNewCard,
    bool IsDue,
    DateTime? NextReviewDate);

public sealed record LearningSession(
    long DeckId,
    string DeckTitle,
    int TotalCards,
    int DueCards,
    int NewCards,
    int CompletedCards,
    int CorrectAnswers,
    IReadOnlyList<LearningCard> Queue,
    LearningCard? CurrentCard,
    IReadOnlyList<ReviewOptionPreview> ReviewPreviews)
{
    public int AccuracyPercentage =>
        CompletedCards == 0 ? 0 : (int)Math.Round((double)CorrectAnswers / CompletedCards * 100, MidpointRounding.AwayFromZero);
}

public sealed record DailyActivityPoint(string Label, int Value);

public sealed record AccuracyPoint(string Label, int Value);

public sealed record ProgressOverview(
    int CurrentStreak,
    int LongestStreak,
    int TotalStudySessions,
    int TotalDurationMinutes,
    int TotalVocabularyCount,
    int NewWordsCount,
    int LearningWordsCount,
    int MasteredWordsCount,
    int AccuracyPercentage,
    int CorrectWords,
    int IncorrectWords,
    int RetentionRate,
    string LevelEstimation,
    IReadOnlyList<DailyActivityPoint> DailyActivity,
    IReadOnlyList<AccuracyPoint> AccuracyTrend);

public sealed record AuthResult(
    bool Succeeded,
    string? ErrorMessage,
    AuthenticatedUser? User,
    string? AccessToken,
    DateTime ExpiresAtUtc)
{
    public static AuthResult Failure(string errorMessage) =>
        new(false, errorMessage, null, null, DateTime.MinValue);

    public static AuthResult Success(AuthenticatedUser user, string accessToken, DateTime expiresAtUtc) =>
        new(true, null, user, accessToken, expiresAtUtc);
}

public sealed record SrsComputation(
    int Interval,
    double EaseFactor,
    int Repetitions,
    DateTime NextReviewDate,
    string Status);

public sealed record LearningReviewResult(
    SrsComputation Schedule,
    LearningSession Session,
    string Message);
