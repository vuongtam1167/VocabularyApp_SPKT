using Microsoft.Data.SqlClient;
using Vocab_LearningApp.Data;
using Vocab_LearningApp.Extensions;
using Vocab_LearningApp.Models.Domain;
using Vocab_LearningApp.Models.Requests;

namespace Vocab_LearningApp.Services;

public sealed class AccountService
{
    private const int DefaultDailyTarget = 10;

    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly PasswordHashingService _passwordHashingService;
    private readonly AuthService _authService;

    public AccountService(
        ISqlConnectionFactory connectionFactory,
        PasswordHashingService passwordHashingService,
        AuthService authService)
    {
        _connectionFactory = connectionFactory;
        _passwordHashingService = passwordHashingService;
        _authService = authService;
    }

    public Task<AuthenticatedUser?> GetProfileAsync(long userId, CancellationToken cancellationToken = default)
    {
        return _authService.GetUserAsync(userId, cancellationToken);
    }

    public async Task<(bool Succeeded, string Message, AuthenticatedUser? UpdatedUser)> UpdateProfileAsync(
        long userId,
        EditProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var fullName = NormalizeRequired(request.FullName);
        var email = NormalizeRequired(request.Email);

        if (fullName is null || email is null)
        {
            return (false, "Họ tên và email là bắt buộc.", null);
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var duplicateCheck = connection.CreateCommand();
        duplicateCheck.CommandText =
            """
            SELECT TOP (1) id
            FROM dbo.Users
            WHERE email = @email
              AND id <> @userId;
            """;
        duplicateCheck.AddParameter("@email", email);
        duplicateCheck.AddParameter("@userId", userId);

        var duplicateId = await duplicateCheck.ExecuteScalarAsync(cancellationToken);
        if (duplicateId is not null)
        {
            return (false, "Email này đã được sử dụng bởi tài khoản khác.", null);
        }

        var updateCommand = connection.CreateCommand();
        updateCommand.CommandText =
            """
            UPDATE dbo.Users
            SET
                full_name = @fullName,
                email = @email,
                avatar_url = @avatarUrl,
                learning_goal = @learningGoal,
                current_level = @currentLevel,
                daily_target = @dailyTarget,
                updated_at = SYSDATETIME()
            WHERE id = @userId;
            """;
        updateCommand.AddParameter("@fullName", fullName);
        updateCommand.AddParameter("@email", email);
        updateCommand.AddParameter("@avatarUrl", NormalizeNullable(request.AvatarUrl));
        updateCommand.AddParameter("@learningGoal", NormalizeNullable(request.LearningGoal));
        updateCommand.AddParameter("@currentLevel", NormalizeNullable(request.CurrentLevel));
        updateCommand.AddParameter("@dailyTarget", request.DailyTarget ?? DefaultDailyTarget);
        updateCommand.AddParameter("@userId", userId);

        var affected = await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        if (affected <= 0)
        {
            return (false, "Không thể cập nhật thông tin.", null);
        }

        var updatedUser = await _authService.GetUserAsync(userId, cancellationToken);
        return updatedUser is null
            ? (false, "Không thể tải lại thông tin tài khoản.", null)
            : (true, "Đã cập nhật thông tin cá nhân.", updatedUser);
    }

    public async Task<(bool Succeeded, string Message)> ChangePasswordAsync(
        long userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var currentPassword = NormalizeRequired(request.CurrentPassword);
        var newPassword = NormalizeRequired(request.NewPassword);
        var confirmPassword = NormalizeRequired(request.ConfirmNewPassword);

        if (currentPassword is null || newPassword is null || confirmPassword is null)
        {
            return (false, "Dữ liệu đổi mật khẩu không hợp lệ.");
        }

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            return (false, "Mật khẩu xác nhận không khớp.");
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var loadCommand = connection.CreateCommand();
        loadCommand.CommandText =
            """
            SELECT password_hash, auth_provider
            FROM dbo.Users
            WHERE id = @userId;
            """;
        loadCommand.AddParameter("@userId", userId);

        string? passwordHash = null;
        string? authProvider = null;

        await using (var reader = await loadCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                passwordHash = reader.GetNullableString("password_hash");
                authProvider = reader.GetNullableString("auth_provider");
            }
        }

        if (!string.Equals(authProvider, "local", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Tài khoản đăng nhập bằng Google chưa có mật khẩu cục bộ.");
        }

        if (string.IsNullOrWhiteSpace(passwordHash) ||
            !_passwordHashingService.VerifyPassword(currentPassword, passwordHash))
        {
            return (false, "Mật khẩu hiện tại không đúng.");
        }

        var updateCommand = connection.CreateCommand();
        updateCommand.CommandText =
            """
            UPDATE dbo.Users
            SET password_hash = @passwordHash,
                updated_at = SYSDATETIME()
            WHERE id = @userId;
            """;
        updateCommand.AddParameter("@passwordHash", _passwordHashingService.HashPassword(newPassword));
        updateCommand.AddParameter("@userId", userId);

        var affected = await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0
            ? (true, "Đã đổi mật khẩu thành công.")
            : (false, "Không thể đổi mật khẩu.");
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeRequired(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}