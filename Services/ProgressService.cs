using Vocab_LearningApp.Data;
using Vocab_LearningApp.Extensions;
using Vocab_LearningApp.Models.Domain;

namespace Vocab_LearningApp.Services;

public sealed class ProgressService
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public ProgressService(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ProgressOverview> GetProgressAsync(long userId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var summary = await GetSummaryAsync(connection, userId, cancellationToken);
        var dailyActivity = await GetDailyActivityAsync(connection, userId, cancellationToken);
        var accuracyTrend = await GetAccuracyTrendAsync(connection, userId, cancellationToken);

        var totalVocabulary = summary.TotalVocabularyCount;
        var learnedWords = summary.LearningWordsCount + summary.MasteredWordsCount;
        var retentionRate = learnedWords == 0
            ? 0
            : (int)Math.Round((double)summary.MasteredWordsCount / learnedWords * 100, MidpointRounding.AwayFromZero);

        var incorrectWords = Math.Max(summary.TotalTrackedWords - summary.CorrectWords, 0);
        var level = EstimateLevel(summary.MasteredWordsCount, summary.AccuracyPercentage);

        return new ProgressOverview(
            summary.CurrentStreak,
            summary.LongestStreak,
            summary.TotalStudySessions,
            summary.TotalDurationMinutes,
            totalVocabulary,
            summary.NewWordsCount,
            summary.LearningWordsCount,
            summary.MasteredWordsCount,
            summary.AccuracyPercentage,
            summary.CorrectWords,
            incorrectWords,
            retentionRate,
            level,
            dailyActivity,
            accuracyTrend);
    }

    private static async Task<ProgressSummaryRecord> GetSummaryAsync(
        Microsoft.Data.SqlClient.SqlConnection connection,
        long userId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                COALESCE(us.current_streak, 0) AS current_streak,
                COALESCE(us.longest_streak, 0) AS longest_streak,
                (SELECT COUNT(*) FROM dbo.Study_Logs sl WHERE sl.user_id = @userId) AS total_study_sessions,
                (SELECT COALESCE(SUM(CASE WHEN duration > 0 THEN duration ELSE 15 END), 0) FROM dbo.Study_Logs sl WHERE sl.user_id = @userId) AS total_duration_minutes,
                (SELECT COUNT(*)
                 FROM dbo.Vocabularies v
                 INNER JOIN dbo.Decks d ON d.id = v.deck_id
                 WHERE d.user_id = @userId) AS total_vocabulary_count,
                (SELECT COUNT(*)
                 FROM dbo.Vocabularies v
                 INNER JOIN dbo.Decks d ON d.id = v.deck_id
                 LEFT JOIN dbo.Learning_Progress lp ON lp.vocabulary_id = v.id AND lp.user_id = @userId
                 WHERE d.user_id = @userId AND lp.id IS NULL) AS new_words_count,
                (SELECT COUNT(*)
                 FROM dbo.Learning_Progress lp
                 WHERE lp.user_id = @userId
                   AND (lp.status IS NULL OR lp.status IN (N'learning', N'reviewing'))) AS learning_words_count,
                (SELECT COUNT(*) FROM dbo.Learning_Progress lp WHERE lp.user_id = @userId AND lp.status = N'mastered') AS mastered_words_count,
                (SELECT COALESCE(SUM(correct_words), 0) FROM dbo.Study_Logs sl WHERE sl.user_id = @userId) AS correct_words,
                (SELECT COALESCE(SUM(total_words), 0) FROM dbo.Study_Logs sl WHERE sl.user_id = @userId) AS total_tracked_words,
                (SELECT COALESCE(AVG(CAST(quality_score AS FLOAT)), 0.0) FROM dbo.Study_Logs sl WHERE sl.user_id = @userId) AS avg_quality
            FROM dbo.Users u
            LEFT JOIN dbo.User_Streaks us ON us.user_id = u.id
            WHERE u.id = @userId;
            """;
        command.AddParameter("@userId", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new ProgressSummaryRecord(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var correctWords = reader.GetInt32(reader.GetOrdinal("correct_words"));
        var totalTrackedWords = reader.GetInt32(reader.GetOrdinal("total_tracked_words"));
        var avgQuality = reader.GetDouble(reader.GetOrdinal("avg_quality"));
        var accuracy = totalTrackedWords > 0
            ? (int)Math.Round((double)correctWords / totalTrackedWords * 100, MidpointRounding.AwayFromZero)
            : (int)Math.Round(avgQuality * 20, MidpointRounding.AwayFromZero);

        return new ProgressSummaryRecord(
            reader.GetInt32(reader.GetOrdinal("current_streak")),
            reader.GetInt32(reader.GetOrdinal("longest_streak")),
            reader.GetInt32(reader.GetOrdinal("total_study_sessions")),
            reader.GetInt32(reader.GetOrdinal("total_duration_minutes")),
            reader.GetInt32(reader.GetOrdinal("total_vocabulary_count")),
            reader.GetInt32(reader.GetOrdinal("new_words_count")),
            reader.GetInt32(reader.GetOrdinal("learning_words_count")),
            reader.GetInt32(reader.GetOrdinal("mastered_words_count")),
            correctWords,
            totalTrackedWords > 0 ? totalTrackedWords : Math.Max(reader.GetInt32(reader.GetOrdinal("total_study_sessions")) * 10, correctWords),
            accuracy);
    }

    private static async Task<IReadOnlyList<DailyActivityPoint>> GetDailyActivityAsync(
        Microsoft.Data.SqlClient.SqlConnection connection,
        long userId,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<DateTime, int>();

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                CAST(learned_at AS DATE) AS study_date,
                SUM(
                    CASE
                        WHEN (new_words + reviewed_words) > 0 THEN (new_words + reviewed_words)
                        WHEN quality_score IS NOT NULL THEN quality_score * 5
                        ELSE 1
                    END
                ) AS study_units
            FROM dbo.Study_Logs
            WHERE user_id = @userId
              AND learned_at >= DATEADD(DAY, -13, CAST(SYSDATETIME() AS DATE))
            GROUP BY CAST(learned_at AS DATE);
            """;
        command.AddParameter("@userId", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values[reader.GetDateTime(reader.GetOrdinal("study_date")).Date] = reader.GetInt32(reader.GetOrdinal("study_units"));
        }

        return Enumerable.Range(0, 14)
            .Select(offset =>
            {
                var date = DateTime.Today.AddDays(offset - 13);
                var label = $"{date.Day}/{date.Month}";
                values.TryGetValue(date, out var value);
                return new DailyActivityPoint(label, value);
            })
            .ToList();
    }

    private static async Task<IReadOnlyList<AccuracyPoint>> GetAccuracyTrendAsync(
        Microsoft.Data.SqlClient.SqlConnection connection,
        long userId,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<DateTime, int>();

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                CAST(learned_at AS DATE) AS study_date,
                CASE
                    WHEN SUM(total_words) > 0
                        THEN CAST(ROUND((CAST(SUM(correct_words) AS FLOAT) / NULLIF(SUM(total_words), 0)) * 100, 0) AS INT)
                    ELSE CAST(ROUND(AVG(CAST(quality_score AS FLOAT)) * 20, 0) AS INT)
                END AS accuracy
            FROM dbo.Study_Logs
            WHERE user_id = @userId
              AND learned_at >= DATEADD(DAY, -13, CAST(SYSDATETIME() AS DATE))
            GROUP BY CAST(learned_at AS DATE);
            """;
        command.AddParameter("@userId", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values[reader.GetDateTime(reader.GetOrdinal("study_date")).Date] = reader.GetInt32(reader.GetOrdinal("accuracy"));
        }

        return Enumerable.Range(0, 14)
            .Select(offset =>
            {
                var date = DateTime.Today.AddDays(offset - 13);
                var label = $"{date.Day}/{date.Month}";
                values.TryGetValue(date, out var value);
                return new AccuracyPoint(label, value);
            })
            .ToList();
    }

    private static string EstimateLevel(int masteredWordsCount, int accuracyPercentage)
    {
        if (masteredWordsCount >= 80 && accuracyPercentage >= 85)
        {
            return "Advanced";
        }

        if (masteredWordsCount >= 30 && accuracyPercentage >= 70)
        {
            return "Intermediate";
        }

        return "Beginner";
    }

    private sealed record ProgressSummaryRecord(
        int CurrentStreak,
        int LongestStreak,
        int TotalStudySessions,
        int TotalDurationMinutes,
        int TotalVocabularyCount,
        int NewWordsCount,
        int LearningWordsCount,
        int MasteredWordsCount,
        int CorrectWords,
        int TotalTrackedWords,
        int AccuracyPercentage);
}
