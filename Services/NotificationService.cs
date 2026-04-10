using Vocab_LearningApp.Data;
using Vocab_LearningApp.Extensions;
using Vocab_LearningApp.Models.Domain;

namespace Vocab_LearningApp.Services;

public sealed class NotificationService
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public NotificationService(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<NotificationSummary>> GetNotificationsAsync(
        long userId,
        int take = 10,
        CancellationToken cancellationToken = default)
    {
        var notifications = new List<NotificationSummary>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT TOP (@take)
                id,
                type,
                content,
                is_read,
                scheduled_at,
                created_at
            FROM dbo.Notifications
            WHERE user_id = @userId
            ORDER BY
                is_read ASC,
                ISNULL(scheduled_at, created_at) ASC,
                created_at DESC;
            """;
        command.AddParameter("@take", take);
        command.AddParameter("@userId", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            notifications.Add(new NotificationSummary(
                reader.GetInt64(reader.GetOrdinal("id")),
                reader.GetInt32(reader.GetOrdinal("type")),
                reader.GetString(reader.GetOrdinal("content")),
                reader.GetBoolean(reader.GetOrdinal("is_read")),
                reader.GetNullableDateTime("scheduled_at"),
                reader.GetDateTime(reader.GetOrdinal("created_at"))));
        }

        return notifications;
    }

    public async Task<bool> MarkAsReadAsync(long userId, long notificationId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE dbo.Notifications
            SET is_read = 1,
                updated_at = SYSDATETIME()
            WHERE id = @notificationId
              AND user_id = @userId;
            """;
        command.AddParameter("@notificationId", notificationId);
        command.AddParameter("@userId", userId);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }
}
