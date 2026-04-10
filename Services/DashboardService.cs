using Vocab_LearningApp.Data;
using Vocab_LearningApp.Extensions;
using Vocab_LearningApp.Models.Domain;

namespace Vocab_LearningApp.Services;

public sealed class DashboardService
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly NotificationService _notificationService;

    public DashboardService(
        ISqlConnectionFactory connectionFactory,
        NotificationService notificationService)
    {
        _connectionFactory = connectionFactory;
        _notificationService = notificationService;
    }

    public async Task<DashboardSummary> GetDashboardAsync(long userId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var stats = await GetDashboardStatsAsync(connection, userId, cancellationToken);
        var recentDecks = await GetRecentDecksAsync(connection, userId, cancellationToken);
        var notifications = await _notificationService.GetNotificationsAsync(userId, 3, cancellationToken);

        var dailyPlan = new DailyLearningPlan(
            stats.DailyTarget,
            Math.Max(stats.DailyTarget - stats.StudiedTodayCount, 0),
            stats.DueReviewCount);

        return new DashboardSummary(stats, dailyPlan, recentDecks, notifications);
    }

    private static async Task<DashboardStats> GetDashboardStatsAsync(
        Microsoft.Data.SqlClient.SqlConnection connection,
        long userId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            DECLARE @Today DATE = CAST(SYSDATETIME() AS DATE);
            SELECT
                (SELECT COUNT(*) FROM dbo.Decks d WHERE d.user_id = @userId) AS deck_count,
                (SELECT COUNT(*)
                 FROM dbo.Vocabularies v
                 INNER JOIN dbo.Decks d ON d.id = v.deck_id
                 WHERE d.user_id = @userId) AS total_vocabulary_count,
                (SELECT COUNT(*) FROM dbo.Learning_Progress lp WHERE lp.user_id = @userId) AS learned_vocabulary_count,
                (SELECT COUNT(*) FROM dbo.Learning_Progress lp WHERE lp.user_id = @userId AND lp.status = N'mastered') AS mastered_vocabulary_count,
                (SELECT COUNT(*)
                 FROM dbo.Learning_Progress lp
                 WHERE lp.user_id = @userId
                   AND lp.next_review_date IS NOT NULL
                   AND lp.next_review_date <= @Today) AS due_review_count,
                COALESCE(us.current_streak, 0) AS current_streak,
                COALESCE(us.longest_streak, 0) AS longest_streak,
                (SELECT COUNT(*)
                 FROM dbo.Learning_Progress lp
                 WHERE lp.user_id = @userId
                   AND lp.last_reviewed_at IS NOT NULL
                   AND CAST(lp.last_reviewed_at AS DATE) = @Today) AS studied_today_count,
                ISNULL(u.daily_target, 10) AS daily_target,
                (SELECT COALESCE(SUM(correct_words), 0) FROM dbo.Study_Logs sl WHERE sl.user_id = @userId) AS correct_words,
                (SELECT COALESCE(SUM(total_words), 0) FROM dbo.Study_Logs sl WHERE sl.user_id = @userId) AS total_words,
                (SELECT COALESCE(AVG(CAST(quality_score AS FLOAT)), 0.0) FROM dbo.Study_Logs sl WHERE sl.user_id = @userId) AS avg_quality
            FROM dbo.Users u
            LEFT JOIN dbo.User_Streaks us ON us.user_id = u.id
            WHERE u.id = @userId;
            """;
        command.AddParameter("@userId", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new DashboardStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 10);
        }

        var correctWords = reader.GetInt32(reader.GetOrdinal("correct_words"));
        var totalWords = reader.GetInt32(reader.GetOrdinal("total_words"));
        var avgQuality = reader.GetDouble(reader.GetOrdinal("avg_quality"));

        var accuracy = totalWords > 0
            ? (int)Math.Round((double)correctWords / totalWords * 100, MidpointRounding.AwayFromZero)
            : (int)Math.Round(avgQuality * 20, MidpointRounding.AwayFromZero);

        return new DashboardStats(
            reader.GetInt32(reader.GetOrdinal("deck_count")),
            reader.GetInt32(reader.GetOrdinal("total_vocabulary_count")),
            reader.GetInt32(reader.GetOrdinal("learned_vocabulary_count")),
            reader.GetInt32(reader.GetOrdinal("mastered_vocabulary_count")),
            reader.GetInt32(reader.GetOrdinal("due_review_count")),
            reader.GetInt32(reader.GetOrdinal("current_streak")),
            reader.GetInt32(reader.GetOrdinal("longest_streak")),
            accuracy,
            reader.GetInt32(reader.GetOrdinal("studied_today_count")),
            reader.GetInt32(reader.GetOrdinal("daily_target")));
    }

    private static async Task<IReadOnlyList<DeckSummary>> GetRecentDecksAsync(
        Microsoft.Data.SqlClient.SqlConnection connection,
        long userId,
        CancellationToken cancellationToken)
    {
        var decks = new List<DeckSummary>();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            DECLARE @Today DATE = CAST(SYSDATETIME() AS DATE);
            SELECT TOP (6)
                d.id,
                d.title,
                d.description,
                d.tags,
                d.is_public,
                d.created_at,
                (SELECT COUNT(*) FROM dbo.Vocabularies v WHERE v.deck_id = d.id) AS total_words,
                (SELECT COUNT(*)
                 FROM dbo.Learning_Progress lp
                 INNER JOIN dbo.Vocabularies v ON v.id = lp.vocabulary_id
                 WHERE lp.user_id = @userId AND v.deck_id = d.id) AS learned_words,
                (SELECT COUNT(*)
                 FROM dbo.Learning_Progress lp
                 INNER JOIN dbo.Vocabularies v ON v.id = lp.vocabulary_id
                 WHERE lp.user_id = @userId AND v.deck_id = d.id AND lp.status = N'mastered') AS mastered_words,
                (SELECT COUNT(*)
                 FROM dbo.Learning_Progress lp
                 INNER JOIN dbo.Vocabularies v ON v.id = lp.vocabulary_id
                 WHERE lp.user_id = @userId
                   AND v.deck_id = d.id
                   AND lp.next_review_date IS NOT NULL
                   AND lp.next_review_date <= @Today) AS due_words
            FROM dbo.Decks d
            WHERE d.user_id = @userId
            ORDER BY d.updated_at DESC, d.created_at DESC;
            """;
        command.AddParameter("@userId", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            decks.Add(new DeckSummary(
                reader.GetInt64(reader.GetOrdinal("id")),
                reader.GetString(reader.GetOrdinal("title")),
                reader.GetNullableString("description"),
                reader.GetNullableString("tags"),
                reader.GetBoolean(reader.GetOrdinal("is_public")),
                reader.GetDateTime(reader.GetOrdinal("created_at")),
                reader.GetInt32(reader.GetOrdinal("total_words")),
                reader.GetInt32(reader.GetOrdinal("learned_words")),
                reader.GetInt32(reader.GetOrdinal("mastered_words")),
                reader.GetInt32(reader.GetOrdinal("due_words"))));
        }

        return decks;
    }
}