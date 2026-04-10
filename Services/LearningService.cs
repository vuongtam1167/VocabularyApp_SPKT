using Microsoft.Data.SqlClient;
using Vocab_LearningApp.Data;
using Vocab_LearningApp.Extensions;
using Vocab_LearningApp.Models.Domain;
using Vocab_LearningApp.Models.Requests;

namespace Vocab_LearningApp.Services;

public sealed class LearningService
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public LearningService(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<LearningSession> GetLearningSessionAsync(
        long userId,
        long? deckId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var selectedDeck = await ResolveDeckAsync(connection, userId, deckId, cancellationToken);
        if (selectedDeck is null)
        {
            return new LearningSession(0, "Chưa có bộ từ", 0, 0, 0, 0, 0, Array.Empty<LearningCard>(), null, Array.Empty<ReviewOptionPreview>());
        }

        var dailyTarget = await GetDailyTargetAsync(connection, userId, cancellationToken);
        var queue = await GetQueueAsync(connection, userId, selectedDeck.Value.Id, dailyTarget, cancellationToken);
        var performance = await GetTodayPerformanceAsync(connection, userId, cancellationToken);
        var currentCard = queue.FirstOrDefault();
        var previews = currentCard is null ? Array.Empty<ReviewOptionPreview>() : BuildReviewPreviews(currentCard);

        return new LearningSession(
            selectedDeck.Value.Id,
            selectedDeck.Value.Title,
            queue.Count,
            queue.Count(card => card.IsDue),
            queue.Count(card => card.IsNewCard),
            performance.TotalWords,
            performance.CorrectWords,
            queue,
            currentCard,
            previews);
    }

    public async Task<LearningReviewResult?> SubmitReviewAsync(
        long userId,
        ReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var reviewCard = await GetReviewCardAsync(connection, userId, request.VocabularyId, cancellationToken);
        if (reviewCard is null)
        {
            return null;
        }

        var schedule = ComputeSchedule(reviewCard.Interval, reviewCard.EaseFactor, reviewCard.Repetitions, request.Rating);
        var isNewWord = reviewCard.IsNewCard;
        var isCorrect = request.Rating != ReviewRating.Again;

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (reviewCard.ProgressId.HasValue)
        {
            var updateProgressCommand = connection.CreateCommand();
            updateProgressCommand.Transaction = (SqlTransaction)transaction;
            updateProgressCommand.CommandText =
                """
                UPDATE dbo.Learning_Progress
                SET
                    interval = @interval,
                    ease_factor = @easeFactor,
                    repetitions = @repetitions,
                    next_review_date = @nextReviewDate,
                    last_reviewed_at = SYSDATETIME(),
                    status = @status,
                    updated_at = SYSDATETIME()
                WHERE id = @progressId;
                """;
            AddProgressParameters(updateProgressCommand, schedule, reviewCard.ProgressId.Value);
            await updateProgressCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            var insertProgressCommand = connection.CreateCommand();
            insertProgressCommand.Transaction = (SqlTransaction)transaction;
            insertProgressCommand.CommandText =
                """
                INSERT INTO dbo.Learning_Progress
                (
                    user_id,
                    vocabulary_id,
                    interval,
                    ease_factor,
                    repetitions,
                    next_review_date,
                    last_reviewed_at,
                    status,
                    created_at,
                    updated_at
                )
                VALUES
                (
                    @userId,
                    @vocabularyId,
                    @interval,
                    @easeFactor,
                    @repetitions,
                    @nextReviewDate,
                    SYSDATETIME(),
                    @status,
                    SYSDATETIME(),
                    SYSDATETIME()
                );
                """;
            insertProgressCommand.AddParameter("@userId", userId);
            insertProgressCommand.AddParameter("@vocabularyId", reviewCard.VocabularyId);
            AddProgressParameters(insertProgressCommand, schedule);
            await insertProgressCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var logCommand = connection.CreateCommand();
        logCommand.Transaction = (SqlTransaction)transaction;
        logCommand.CommandText =
            """
            INSERT INTO dbo.Study_Logs
            (
                user_id,
                learned_at,
                quality_score,
                new_words,
                reviewed_words,
                correct_words,
                total_words,
                duration,
                created_at,
                updated_at
            )
            VALUES
            (
                @userId,
                SYSDATETIME(),
                @qualityScore,
                @newWords,
                @reviewedWords,
                @correctWords,
                1,
                @duration,
                SYSDATETIME(),
                SYSDATETIME()
            );
            """;
        logCommand.AddParameter("@userId", userId);
        logCommand.AddParameter("@qualityScore", MapQualityScore(request.Rating));
        logCommand.AddParameter("@newWords", isNewWord ? 1 : 0);
        logCommand.AddParameter("@reviewedWords", isNewWord ? 0 : 1);
        logCommand.AddParameter("@correctWords", isCorrect ? 1 : 0);
        logCommand.AddParameter("@duration", isNewWord ? 2 : 1);
        await logCommand.ExecuteNonQueryAsync(cancellationToken);

        await UpdateStreakAsync(connection, (SqlTransaction)transaction, userId, cancellationToken);

        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        var session = await GetLearningSessionAsync(userId, request.DeckId ?? reviewCard.DeckId, cancellationToken);

        return new LearningReviewResult(
            schedule,
            session,
            $"Đã ghi nhận đánh giá '{GetRatingLabel(request.Rating)}' cho từ '{reviewCard.Word}'.");
    }

    private static async Task<(long Id, string Title)?> ResolveDeckAsync(
        SqlConnection connection,
        long userId,
        long? preferredDeckId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            DECLARE @Today DATE = CAST(SYSDATETIME() AS DATE);

            SELECT TOP (1)
                d.id,
                d.title
            FROM dbo.Decks d
            WHERE d.user_id = @userId
              AND (@preferredDeckId IS NULL OR d.id = @preferredDeckId)
            ORDER BY
                (SELECT COUNT(*)
                 FROM dbo.Learning_Progress lp
                 INNER JOIN dbo.Vocabularies v ON v.id = lp.vocabulary_id
                 WHERE lp.user_id = @userId
                   AND v.deck_id = d.id
                   AND lp.next_review_date IS NOT NULL
                   AND lp.next_review_date <= @Today) DESC,
                d.updated_at DESC,
                d.created_at DESC;
            """;
        command.AddParameter("@userId", userId);
        command.AddParameter("@preferredDeckId", preferredDeckId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (reader.GetInt64(reader.GetOrdinal("id")), reader.GetString(reader.GetOrdinal("title")));
    }

    private static async Task<int> GetDailyTargetAsync(SqlConnection connection, long userId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT ISNULL(daily_target, 10) FROM dbo.Users WHERE id = @userId;";
        command.AddParameter("@userId", userId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<List<LearningCard>> GetQueueAsync(
        SqlConnection connection,
        long userId,
        long deckId,
        int dailyTarget,
        CancellationToken cancellationToken)
    {
        var take = Math.Max(dailyTarget + 10, 20);
        var queue = await LoadQueueAsync(connection, userId, deckId, take, dueOrNewOnly: true, cancellationToken);
        if (queue.Count == 0)
        {
            queue = await LoadQueueAsync(connection, userId, deckId, take, dueOrNewOnly: false, cancellationToken);
        }

        return queue;
    }

    private static async Task<List<LearningCard>> LoadQueueAsync(
        SqlConnection connection,
        long userId,
        long deckId,
        int take,
        bool dueOrNewOnly,
        CancellationToken cancellationToken)
    {
        var queue = new List<LearningCard>();
        var command = connection.CreateCommand();

        var additionalFilter = dueOrNewOnly
            ? """
              AND (
                      lp.id IS NULL
                   OR lp.next_review_date IS NULL
                   OR lp.next_review_date <= @today
              )
              """
            : string.Empty;

        command.CommandText =
            $"""
            DECLARE @today DATE = CAST(SYSDATETIME() AS DATE);

            SELECT TOP (@take)
                v.id AS vocabulary_id,
                d.id AS deck_id,
                d.title AS deck_title,
                v.word,
                v.pronunciation,
                v.meaning_vi,
                v.description_en,
                v.example_sentence,
                v.collocations,
                v.related_words,
                v.note,
                COALESCE(lp.status, N'new') AS status,
                COALESCE(lp.interval, 0) AS interval,
                COALESCE(lp.ease_factor, 2.5) AS ease_factor,
                COALESCE(lp.repetitions, 0) AS repetitions,
                CASE WHEN lp.id IS NULL THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS is_new_card,
                CASE WHEN lp.next_review_date IS NOT NULL AND lp.next_review_date <= @today THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS is_due,
                lp.next_review_date
            FROM dbo.Vocabularies v
            INNER JOIN dbo.Decks d ON d.id = v.deck_id
            LEFT JOIN dbo.Learning_Progress lp ON lp.vocabulary_id = v.id AND lp.user_id = @userId
            WHERE d.user_id = @userId
              AND d.id = @deckId
              {additionalFilter}
            ORDER BY
                CASE
                    WHEN lp.next_review_date IS NOT NULL AND lp.next_review_date <= @today THEN 0
                    WHEN lp.id IS NULL THEN 1
                    ELSE 2
                END,
                ISNULL(lp.next_review_date, DATEADD(YEAR, 100, @today)),
                v.created_at;
            """;
        command.AddParameter("@take", take);
        command.AddParameter("@userId", userId);
        command.AddParameter("@deckId", deckId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            queue.Add(new LearningCard(
                reader.GetInt64(reader.GetOrdinal("vocabulary_id")),
                reader.GetInt64(reader.GetOrdinal("deck_id")),
                reader.GetString(reader.GetOrdinal("deck_title")),
                reader.GetString(reader.GetOrdinal("word")),
                reader.GetNullableString("pronunciation"),
                reader.GetNullableString("meaning_vi"),
                reader.GetNullableString("description_en"),
                reader.GetNullableString("example_sentence"),
                reader.GetNullableString("collocations"),
                reader.GetNullableString("related_words"),
                reader.GetNullableString("note"),
                reader.GetString(reader.GetOrdinal("status")),
                reader.GetInt32(reader.GetOrdinal("interval")),
                reader.GetDouble(reader.GetOrdinal("ease_factor")),
                reader.GetInt32(reader.GetOrdinal("repetitions")),
                reader.GetBoolean(reader.GetOrdinal("is_new_card")),
                reader.GetBoolean(reader.GetOrdinal("is_due")),
                reader.GetNullableDateTime("next_review_date")));
        }

        return queue;
    }

    private static async Task<(int TotalWords, int CorrectWords)> GetTodayPerformanceAsync(
        SqlConnection connection,
        long userId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            DECLARE @today DATE = CAST(SYSDATETIME() AS DATE);

            SELECT
                COALESCE(SUM(total_words), 0) AS total_words,
                COALESCE(SUM(correct_words), 0) AS correct_words
            FROM dbo.Study_Logs
            WHERE user_id = @userId
              AND CAST(learned_at AS DATE) = @today;
            """;
        command.AddParameter("@userId", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (0, 0);
        }

        return (
            reader.GetInt32(reader.GetOrdinal("total_words")),
            reader.GetInt32(reader.GetOrdinal("correct_words")));
    }

    private static IReadOnlyList<ReviewOptionPreview> BuildReviewPreviews(LearningCard card)
    {
        return Enum.GetValues<ReviewRating>()
            .Select(rating =>
            {
                var simulated = ComputeSchedule(card.Interval, card.EaseFactor, card.Repetitions, rating);
                return new ReviewOptionPreview(
                    rating,
                    GetRatingLabel(rating),
                    GetRatingEmoji(rating),
                    FormatScheduleText(simulated.NextReviewDate));
            })
            .ToList();
    }

    private static SrsComputation ComputeSchedule(int currentInterval, double currentEaseFactor, int currentRepetitions, ReviewRating rating)
    {
        var quality = MapQualityScore(rating);
        var repetitions = currentRepetitions;
        int interval;

        var easeFactor = currentEaseFactor;

        if (quality < 3)
        {
            repetitions = 0;
            interval = 1;
            easeFactor = Math.Max(1.3, easeFactor - 0.2);
        }
        else
        {
            easeFactor = Math.Max(1.3,
                easeFactor + (0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02)));

            repetitions++;

            if (repetitions == 1)
                interval = 1;
            else if (repetitions == 2)
                interval = rating == ReviewRating.Hard ? 3 : 6;
            else
            {
                interval = (int)Math.Round(currentInterval * easeFactor);

                if (rating == ReviewRating.Hard)
                    interval = Math.Max(2, (int)(interval * 0.8));
                else if (rating == ReviewRating.Easy)
                {
                    interval = (int)(interval * 1.3);
                    easeFactor += 0.1;
                }
            }
        }

        var nextReviewDate = DateTime.UtcNow.Date.AddDays(Math.Max(interval, 1));
        var status = repetitions switch
        {
            >= 3 when interval >= 7 => "mastered",
            >= 2 => "reviewing",
            _ => "learning"
        };

        return new SrsComputation(interval, Math.Round(easeFactor, 2), repetitions, nextReviewDate, status);
    }

    private static async Task<ReviewCardRecord?> GetReviewCardAsync(
        SqlConnection connection,
        long userId,
        long vocabularyId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            DECLARE @today DATE = CAST(SYSDATETIME() AS DATE);

            SELECT TOP (1)
                lp.id AS progress_id,
                v.id AS vocabulary_id,
                d.id AS deck_id,
                v.word,
                COALESCE(lp.interval, 0) AS interval,
                COALESCE(lp.ease_factor, 2.5) AS ease_factor,
                COALESCE(lp.repetitions, 0) AS repetitions,
                CASE WHEN lp.id IS NULL THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS is_new_card
            FROM dbo.Vocabularies v
            INNER JOIN dbo.Decks d ON d.id = v.deck_id
            LEFT JOIN dbo.Learning_Progress lp ON lp.vocabulary_id = v.id AND lp.user_id = @userId
            WHERE v.id = @vocabularyId
              AND d.user_id = @userId;
            """;
        command.AddParameter("@userId", userId);
        command.AddParameter("@vocabularyId", vocabularyId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ReviewCardRecord(
            reader.GetNullableInt64("progress_id"),
            reader.GetInt64(reader.GetOrdinal("vocabulary_id")),
            reader.GetInt64(reader.GetOrdinal("deck_id")),
            reader.GetString(reader.GetOrdinal("word")),
            reader.GetInt32(reader.GetOrdinal("interval")),
            reader.GetDouble(reader.GetOrdinal("ease_factor")),
            reader.GetInt32(reader.GetOrdinal("repetitions")),
            reader.GetBoolean(reader.GetOrdinal("is_new_card")));
    }

    private static void AddProgressParameters(SqlCommand command, SrsComputation schedule, long? progressId = null)
    {
        command.AddParameter("@interval", schedule.Interval);
        command.AddParameter("@easeFactor", schedule.EaseFactor);
        command.AddParameter("@repetitions", schedule.Repetitions);
        command.AddParameter("@nextReviewDate", schedule.NextReviewDate);
        command.AddParameter("@status", schedule.Status);

        if (progressId.HasValue)
        {
            command.AddParameter("@progressId", progressId.Value);
        }
    }

    private static async Task UpdateStreakAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long userId,
        CancellationToken cancellationToken)
    {
        var selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText =
            """
            SELECT current_streak, longest_streak, last_study_date
            FROM dbo.User_Streaks
            WHERE user_id = @userId;
            """;
        selectCommand.AddParameter("@userId", userId);

        int currentStreak = 0;
        int longestStreak = 0;
        DateTime? lastStudyDate = null;

        await using (var reader = await selectCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                currentStreak = reader.GetInt32(reader.GetOrdinal("current_streak"));
                longestStreak = reader.GetInt32(reader.GetOrdinal("longest_streak"));
                lastStudyDate = reader.GetNullableDateTime("last_study_date")?.Date;
            }
        }

        var today = DateTime.UtcNow.Date;
        if (lastStudyDate == today)
        {
            return;
        }

        currentStreak = lastStudyDate == today.AddDays(-1) ? currentStreak + 1 : 1;
        longestStreak = Math.Max(longestStreak, currentStreak);

        var upsertCommand = connection.CreateCommand();
        upsertCommand.Transaction = transaction;
        upsertCommand.CommandText =
            """
            MERGE dbo.User_Streaks AS target
            USING (SELECT @userId AS user_id) AS source
            ON target.user_id = source.user_id
            WHEN MATCHED THEN
                UPDATE SET
                    current_streak = @currentStreak,
                    longest_streak = @longestStreak,
                    last_study_date = @today
            WHEN NOT MATCHED THEN
                INSERT (user_id, current_streak, longest_streak, last_study_date)
                VALUES (@userId, @currentStreak, @longestStreak, @today);
            """;
        upsertCommand.AddParameter("@userId", userId);
        upsertCommand.AddParameter("@currentStreak", currentStreak);
        upsertCommand.AddParameter("@longestStreak", longestStreak);
        upsertCommand.AddParameter("@today", today);
        await upsertCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static int MapQualityScore(ReviewRating rating) =>
        rating switch
        {
            ReviewRating.Again => 0,
            ReviewRating.Hard => 3,
            ReviewRating.Good => 4,
            ReviewRating.Easy => 5,
            _ => 4
        };

    private static string GetRatingLabel(ReviewRating rating) =>
        rating switch
        {
            ReviewRating.Again => "Quên",
            ReviewRating.Hard => "Khó",
            ReviewRating.Good => "Tốt",
            ReviewRating.Easy => "Dễ",
            _ => "Tốt"
        };

    private static string GetRatingEmoji(ReviewRating rating) =>
        rating switch
        {
            ReviewRating.Again => "😞",
            ReviewRating.Hard => "😐",
            ReviewRating.Good => "😊",
            ReviewRating.Easy => "😄",
            _ => "😊"
        };

    private static string FormatScheduleText(DateTime nextReviewDate)
    {
        var days = Math.Max((nextReviewDate.Date - DateTime.UtcNow.Date).Days, 1);
        return days switch
        {
            0 => "Hôm nay",
            1 => "1 ngày",
            _ => $"{days} ngày"
        };
    }

    private sealed record ReviewCardRecord(
        long? ProgressId,
        long VocabularyId,
        long DeckId,
        string Word,
        int Interval,
        double EaseFactor,
        int Repetitions,
        bool IsNewCard);
}
