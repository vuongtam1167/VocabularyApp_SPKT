using Microsoft.Data.SqlClient;
using Vocab_LearningApp.Data;
using Vocab_LearningApp.Extensions;
using Vocab_LearningApp.Models.Domain;
using Vocab_LearningApp.Models.Requests;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;

namespace Vocab_LearningApp.Services;

public sealed class AuthService
{
    private const int DefaultDailyTarget = 10;

    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly PasswordHashingService _passwordHashingService;
    private readonly JwtTokenService _jwtTokenService;
    private readonly string _googleClientId;

    public AuthService(
        ISqlConnectionFactory connectionFactory,
        PasswordHashingService passwordHashingService,
        JwtTokenService jwtTokenService,
        IConfiguration configuration)
    {
        _connectionFactory = connectionFactory;
        _passwordHashingService = passwordHashingService;
        _jwtTokenService = jwtTokenService;
        _googleClientId = configuration["Google:ClientId"]?.Trim() ?? string.Empty;
    }

    public async Task<AuthResult> LoginAsync(LoginInputModel request, CancellationToken cancellationToken = default)
    {
        var email = NormalizeRequired(request.Email);
        if (email is null)
        {
            return AuthResult.Failure("Email hoặc mật khẩu không chính xác.");
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var userRecord = await GetUserAuthRecordByEmailAsync(connection, email, cancellationToken);
        if (userRecord is null)
        {
            return AuthResult.Failure("Email hoặc mật khẩu không chính xác.");
        }

        if (!string.Equals(userRecord.AuthProvider, "local", StringComparison.OrdinalIgnoreCase))
        {
            return AuthResult.Failure("Tài khoản này dùng Google đăng nhập. Hãy dùng Google login API.");
        }

        if (string.IsNullOrWhiteSpace(userRecord.PasswordHash)
            || !_passwordHashingService.VerifyPassword(request.Password, userRecord.PasswordHash))
        {
            return AuthResult.Failure("Email hoặc mật khẩu không chính xác.");
        }

        var user = ToAuthenticatedUser(userRecord);
        var (token, expiresAtUtc) = _jwtTokenService.CreateToken(user);
        return AuthResult.Success(user, token, expiresAtUtc);
    }

    public async Task<AuthResult> RegisterAsync(RegisterInputModel request, CancellationToken cancellationToken = default)
    {
        if (!request.AcceptTerms)
        {
            return AuthResult.Failure("Bạn cần đồng ý điều khoản dịch vụ để đăng ký.");
        }

        var fullName = NormalizeRequired(request.FullName);
        var email = NormalizeRequired(request.Email);
        var password = NormalizeRequired(request.Password);

        if (fullName is null || email is null || password is null)
        {
            return AuthResult.Failure("Thông tin đăng ký không hợp lệ.");
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var existingUser = await GetUserAuthRecordByEmailAsync(connection, email, cancellationToken);
        if (existingUser is not null)
        {
            return AuthResult.Failure("Email này đã tồn tại.");
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var command = connection.CreateCommand();
            command.Transaction = (SqlTransaction)transaction;
            command.CommandText =
                """
                INSERT INTO dbo.Users
                (
                    full_name,
                    email,
                    password_hash,
                    auth_provider,
                    avatar_url,
                    learning_goal,
                    current_level,
                    daily_target,
                    created_at,
                    updated_at
                )
                OUTPUT INSERTED.id
                VALUES
                (
                    @fullName,
                    @email,
                    @passwordHash,
                    N'local',
                    NULL,
                    @learningGoal,
                    @currentLevel,
                    @dailyTarget,
                    SYSDATETIME(),
                    SYSDATETIME()
                );
                """;

            command.AddParameter("@fullName", fullName);
            command.AddParameter("@email", email);
            command.AddParameter("@passwordHash", _passwordHashingService.HashPassword(password));
            command.AddParameter("@learningGoal", NormalizeNullable(request.LearningGoal));
            command.AddParameter("@currentLevel", NormalizeNullable(request.CurrentLevel));
            command.AddParameter("@dailyTarget", request.DailyTarget ?? DefaultDailyTarget);

            var insertedId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));

            await EnsureStreakRowAsync(connection, (SqlTransaction)transaction, insertedId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var user = new AuthenticatedUser(
                insertedId,
                fullName,
                email,
                "local",
                null,
                NormalizeNullable(request.LearningGoal),
                NormalizeNullable(request.CurrentLevel),
                request.DailyTarget ?? DefaultDailyTarget);

            var (token, expiresAtUtc) = _jwtTokenService.CreateToken(user);
            return AuthResult.Success(user, token, expiresAtUtc);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<AuthResult> GoogleLoginAsync(GoogleLoginRequest request, CancellationToken cancellationToken = default)
    {
        var credential = NormalizeRequired(request.Credential);
        if (credential is null)
        {
            return AuthResult.Failure("Thông tin đăng nhập Google không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(_googleClientId))
        {
            return AuthResult.Failure("Chưa cấu hình Google Client ID.");
        }

        GoogleJsonWebSignature.Payload payload;

        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(
                credential,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _googleClientId }
                });
        }
        catch (InvalidJwtException)
        {
            return AuthResult.Failure("Google token không hợp lệ hoặc đã hết hạn.");
        }

        var email = NormalizeRequired(payload.Email);
        var fullName = NormalizeNullable(payload.Name) ?? email;
        var googleSub = NormalizeRequired(payload.Subject);
        var avatarUrl = NormalizeNullable(payload.Picture);

        if (email is null || fullName is null || googleSub is null)
        {
            return AuthResult.Failure("Google token thiếu thông tin cần thiết.");
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var existingByGoogleSub = await GetUserAuthRecordByGoogleSubAsync(connection, googleSub, cancellationToken);
        var existingByEmail = await GetUserAuthRecordByEmailAsync(connection, email, cancellationToken);

        if (existingByEmail is not null
            && existingByGoogleSub is not null
            && existingByEmail.Id != existingByGoogleSub.Id)
        {
            return AuthResult.Failure("Email này đã được liên kết với một tài khoản khác.");
        }

        if (existingByEmail is not null
            && !string.IsNullOrWhiteSpace(existingByEmail.GoogleSub)
            && !string.Equals(existingByEmail.GoogleSub, googleSub, StringComparison.Ordinal))
        {
            return AuthResult.Failure("Email này đã được liên kết với một tài khoản Google khác.");
        }

        var targetRecord = existingByGoogleSub ?? existingByEmail;

        if (targetRecord is null)
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                var createCommand = connection.CreateCommand();
                createCommand.Transaction = (SqlTransaction)transaction;
                createCommand.CommandText =
                    """
                INSERT INTO dbo.Users
                (
                    full_name,
                    email,
                    password_hash,
                    auth_provider,
                    google_sub,
                    avatar_url,
                    learning_goal,
                    current_level,
                    daily_target,
                    created_at,
                    updated_at
                )
                OUTPUT INSERTED.id
                VALUES
                (
                    @fullName,
                    @email,
                    NULL,
                    N'google',
                    @googleSub,
                    @avatarUrl,
                    NULL,
                    NULL,
                    @dailyTarget,
                    SYSDATETIME(),
                    SYSDATETIME()
                );
                """;

                createCommand.AddParameter("@fullName", fullName);
                createCommand.AddParameter("@email", email);
                createCommand.AddParameter("@googleSub", googleSub);
                createCommand.AddParameter("@avatarUrl", avatarUrl);
                createCommand.AddParameter("@dailyTarget", DefaultDailyTarget);

                var userId = Convert.ToInt64(await createCommand.ExecuteScalarAsync(cancellationToken));

                await EnsureStreakRowAsync(connection, (SqlTransaction)transaction, userId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var user = new AuthenticatedUser(
                    userId,
                    fullName,
                    email,
                    "google",
                    avatarUrl,
                    null,
                    null,
                    DefaultDailyTarget);

                var (token, expiresAtUtc) = _jwtTokenService.CreateToken(user);
                return AuthResult.Success(user, token, expiresAtUtc);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        var updatedAvatarUrl = avatarUrl ?? targetRecord.AvatarUrl;
        var updatedAuthProvider = targetRecord.AuthProvider;

        var updateCommand = connection.CreateCommand();
        updateCommand.CommandText =
            """
        UPDATE dbo.Users
        SET
            full_name = @fullName,
            email = @email,
            auth_provider = @authProvider,
            google_sub = CASE
                WHEN google_sub IS NULL THEN @googleSub
                ELSE google_sub
            END,
            avatar_url = @avatarUrl,
            updated_at = SYSDATETIME()
        WHERE id = @userId;
        """;

        updateCommand.AddParameter("@fullName", fullName);
        updateCommand.AddParameter("@email", email);
        updateCommand.AddParameter("@authProvider", updatedAuthProvider);
        updateCommand.AddParameter("@googleSub", googleSub);
        updateCommand.AddParameter("@avatarUrl", updatedAvatarUrl);
        updateCommand.AddParameter("@userId", targetRecord.Id);

        await updateCommand.ExecuteNonQueryAsync(cancellationToken);

        var updatedUser = new AuthenticatedUser(
            targetRecord.Id,
            fullName,
            email,
            updatedAuthProvider,
            updatedAvatarUrl,
            targetRecord.LearningGoal,
            targetRecord.CurrentLevel,
            targetRecord.DailyTarget);

        var (updatedToken, updatedExpiresAtUtc) = _jwtTokenService.CreateToken(updatedUser);
        return AuthResult.Success(updatedUser, updatedToken, updatedExpiresAtUtc);
    }

    public async Task<AuthenticatedUser?> GetUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                full_name,
                email,
                auth_provider,
                avatar_url,
                learning_goal,
                current_level,
                ISNULL(daily_target, @defaultDailyTarget) AS daily_target
            FROM dbo.Users
            WHERE id = @userId;
            """;
        command.AddParameter("@userId", userId);
        command.AddParameter("@defaultDailyTarget", DefaultDailyTarget);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AuthenticatedUser(
            reader.GetInt64(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("full_name")),
            reader.GetString(reader.GetOrdinal("email")),
            reader.GetString(reader.GetOrdinal("auth_provider")),
            reader.GetNullableString("avatar_url"),
            reader.GetNullableString("learning_goal"),
            reader.GetNullableString("current_level"),
            reader.GetInt32(reader.GetOrdinal("daily_target")));
    }

    private static AuthenticatedUser ToAuthenticatedUser(UserAuthRecord userRecord) =>
        new(
            userRecord.Id,
            userRecord.FullName,
            userRecord.Email,
            userRecord.AuthProvider,
            userRecord.AvatarUrl,
            userRecord.LearningGoal,
            userRecord.CurrentLevel,
            userRecord.DailyTarget);

    private async Task<UserAuthRecord?> GetUserAuthRecordByEmailAsync(
        SqlConnection connection,
        string email,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = UserLookupSql + " WHERE email = @lookupValue;";
        command.AddParameter("@lookupValue", email.Trim());
        command.AddParameter("@defaultDailyTarget", DefaultDailyTarget);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await ReadUserAuthRecordAsync(reader, cancellationToken);
    }

    private async Task<UserAuthRecord?> GetUserAuthRecordByGoogleSubAsync(
        SqlConnection connection,
        string googleSub,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = UserLookupSql + " WHERE google_sub = @lookupValue;";
        command.AddParameter("@lookupValue", googleSub.Trim());
        command.AddParameter("@defaultDailyTarget", DefaultDailyTarget);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await ReadUserAuthRecordAsync(reader, cancellationToken);
    }

    private static async Task<UserAuthRecord?> ReadUserAuthRecordAsync(SqlDataReader reader, CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new UserAuthRecord(
            reader.GetInt64(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("full_name")),
            reader.GetString(reader.GetOrdinal("email")),
            reader.GetNullableString("password_hash"),
            reader.GetString(reader.GetOrdinal("auth_provider")),
            reader.GetNullableString("google_sub"),
            reader.GetNullableString("avatar_url"),
            reader.GetNullableString("learning_goal"),
            reader.GetNullableString("current_level"),
            reader.GetInt32(reader.GetOrdinal("daily_target")));
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeRequired(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task EnsureStreakRowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long userId,
        CancellationToken cancellationToken)
    {
        var streakCommand = connection.CreateCommand();
        streakCommand.Transaction = transaction;
        streakCommand.CommandText =
            """
            IF NOT EXISTS (SELECT 1 FROM dbo.User_Streaks WHERE user_id = @userId)
            BEGIN
                INSERT INTO dbo.User_Streaks(user_id, current_streak, longest_streak, last_study_date)
                VALUES(@userId, 0, 0, NULL);
            END
            """;
        streakCommand.AddParameter("@userId", userId);
        await streakCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string UserLookupSql =
        """
        SELECT
            id,
            full_name,
            email,
            password_hash,
            auth_provider,
            google_sub,
            avatar_url,
            learning_goal,
            current_level,
            ISNULL(daily_target, @defaultDailyTarget) AS daily_target
        FROM dbo.Users
        """;

    private sealed record UserAuthRecord(
        long Id,
        string FullName,
        string Email,
        string? PasswordHash,
        string AuthProvider,
        string? GoogleSub,
        string? AvatarUrl,
        string? LearningGoal,
        string? CurrentLevel,
        int DailyTarget);
}