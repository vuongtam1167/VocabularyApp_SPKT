using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using System.Text;
using Vocab_LearningApp.Data;
using Vocab_LearningApp.Extensions;
using Vocab_LearningApp.Models.Domain;
using Vocab_LearningApp.Models.Requests;

namespace Vocab_LearningApp.Services;

public sealed class DeckService
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public DeckService(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<DeckSummary>> GetDecksAsync(
        long userId,
        string? search,
        string? tag,
        string? sort,
        CancellationToken cancellationToken = default)
    {
        var decks = new List<DeckSummary>();
        var whereClauses = new List<string> { "d.user_id = @userId" };
        var orderBy = ResolveDeckSort(sort);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();

        if (!string.IsNullOrWhiteSpace(search))
        {
            whereClauses.Add("(d.title LIKE @search OR d.description LIKE @search OR d.tags LIKE @search)");
            command.AddParameter("@search", $"%{search.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            whereClauses.Add("d.tags LIKE @tag");
            command.AddParameter("@tag", $"%{tag.Trim()}%");
        }

        command.CommandText =
            $"""
            DECLARE @Today DATE = CAST(SYSDATETIME() AS DATE);
            SELECT
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
            WHERE {string.Join(" AND ", whereClauses)}
            ORDER BY {orderBy};
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

    public async Task<DeckDetail?> GetDeckDetailAsync(
        long userId,
        long deckId,
        string? search,
        string? status,
        string? sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Max(pageSize, 1);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var deck = await GetDeckInfoAsync(connection, userId, deckId, cancellationToken);
        if (deck is null)
        {
            return null;
        }

        var (items, totalItems) = await GetVocabularyItemsAsync(
            connection,
            userId,
            deckId,
            search,
            status,
            sort,
            page,
            pageSize,
            cancellationToken);

        return deck with
        {
            VocabularyItems = items,
            Pagination = new PaginationInfo(page, pageSize, totalItems)
        };
    }

    public async Task<long> CreateDeckAsync(long userId, CreateDeckRequest request, CancellationToken cancellationToken = default)
    {
        var title = NormalizeRequired(request.Title);
        if (title is null)
        {
            throw new InvalidOperationException("Title is required.");
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO dbo.Decks(user_id, title, description, is_public, tags, created_at, updated_at)
            OUTPUT INSERTED.id
            VALUES(@userId, @title, @description, @isPublic, @tags, SYSDATETIME(), SYSDATETIME());
            """;
        command.AddParameter("@userId", userId);
        command.AddParameter("@title", title);
        command.AddParameter("@description", NormalizeNullable(request.Description));
        command.AddParameter("@isPublic", request.IsPublic);
        command.AddParameter("@tags", NormalizeNullable(request.Tags));

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<bool> UpdateDeckAsync(long userId, long deckId, UpdateDeckRequest request, CancellationToken cancellationToken = default)
    {
        var title = NormalizeRequired(request.Title);
        if (title is null)
        {
            throw new InvalidOperationException("Title is required.");
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE dbo.Decks
            SET
                title = @title,
                description = @description,
                is_public = @isPublic,
                tags = @tags,
                updated_at = SYSDATETIME()
            WHERE id = @deckId AND user_id = @userId;
            """;
        command.AddParameter("@deckId", deckId);
        command.AddParameter("@userId", userId);
        command.AddParameter("@title", title);
        command.AddParameter("@description", NormalizeNullable(request.Description));
        command.AddParameter("@isPublic", request.IsPublic);
        command.AddParameter("@tags", NormalizeNullable(request.Tags));

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> DeleteDeckAsync(long userId, long deckId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM dbo.Decks WHERE id = @deckId AND user_id = @userId;";
        command.AddParameter("@deckId", deckId);
        command.AddParameter("@userId", userId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<long?> CreateVocabularyAsync(
        long userId,
        long deckId,
        CreateVocabularyRequest request,
        CancellationToken cancellationToken = default)
    {
        var word = NormalizeRequired(request.Word);
        if (word is null)
        {
            throw new InvalidOperationException("Word is required.");
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (!await DeckExistsAsync(connection, userId, deckId, cancellationToken))
        {
            return null;
        }

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO dbo.Vocabularies
            (
                deck_id,
                word,
                pronunciation,
                pos,
                meaning_vi,
                description_en,
                example_sentence,
                collocations,
                related_words,
                note,
                image_url,
                audio_url,
                created_at,
                updated_at
            )
            OUTPUT INSERTED.id
            VALUES
            (
                @deckId,
                @word,
                @pronunciation,
                @partOfSpeech,
                @meaningVi,
                @descriptionEn,
                @exampleSentence,
                @collocations,
                @relatedWords,
                @note,
                @imageUrl,
                @audioUrl,
                SYSDATETIME(),
                SYSDATETIME()
            );
            """;
        AddVocabularyParameters(command, deckId, request, word);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<bool> UpdateVocabularyAsync(
        long userId,
        long vocabularyId,
        UpdateVocabularyRequest request,
        CancellationToken cancellationToken = default)
    {
        var word = NormalizeRequired(request.Word);
        if (word is null)
        {
            throw new InvalidOperationException("Word is required.");
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE v
            SET
                v.word = @word,
                v.pronunciation = @pronunciation,
                v.pos = @partOfSpeech,
                v.meaning_vi = @meaningVi,
                v.description_en = @descriptionEn,
                v.example_sentence = @exampleSentence,
                v.collocations = @collocations,
                v.related_words = @relatedWords,
                v.note = @note,
                v.image_url = @imageUrl,
                v.audio_url = @audioUrl,
                v.updated_at = SYSDATETIME()
            FROM dbo.Vocabularies v
            INNER JOIN dbo.Decks d ON d.id = v.deck_id
            WHERE v.id = @vocabularyId
              AND d.user_id = @userId;
            """;
        command.AddParameter("@vocabularyId", vocabularyId);
        command.AddParameter("@userId", userId);
        AddVocabularyParameters(command, 0, request, word, includeDeckId: false);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> DeleteVocabularyAsync(long userId, long vocabularyId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE v
            FROM dbo.Vocabularies v
            INNER JOIN dbo.Decks d ON d.id = v.deck_id
            WHERE v.id = @vocabularyId
              AND d.user_id = @userId;
            """;
        command.AddParameter("@vocabularyId", vocabularyId);
        command.AddParameter("@userId", userId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<(byte[] Content, string ContentType, string FileName)?> ExportDeckAsync(
        long userId,
        long deckId,
        string format,
        CancellationToken cancellationToken = default)
    {
        var deckDetail = await GetDeckDetailAsync(userId, deckId, null, null, "az", 1, 5000, cancellationToken);
        if (deckDetail is null)
        {
            return null;
        }

        var safeTitle = deckDetail.Title.Replace(' ', '_');
        if (string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase))
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Vocabulary");
            WriteExportHeaders(worksheet);

            for (var index = 0; index < deckDetail.VocabularyItems.Count; index++)
            {
                var item = deckDetail.VocabularyItems[index];
                WriteExportRow(worksheet, index + 2, item);
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return (
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"{safeTitle}.xlsx");
        }

        var builder = new StringBuilder();
        builder.AppendLine("Word,Pronunciation,PartOfSpeech,MeaningVi,DescriptionEn,ExampleSentence,Collocations,RelatedWords,Note,ImageUrl,AudioUrl");
        foreach (var item in deckDetail.VocabularyItems)
        {
            builder.AppendLine(string.Join(",",
                CsvEscape(item.Word),
                CsvEscape(item.Pronunciation),
                CsvEscape(item.PartOfSpeech?.ToString()),
                CsvEscape(item.MeaningVi),
                CsvEscape(item.DescriptionEn),
                CsvEscape(item.ExampleSentence),
                CsvEscape(item.Collocations),
                CsvEscape(item.RelatedWords),
                CsvEscape(item.Note),
                CsvEscape(item.ImageUrl),
                CsvEscape(item.AudioUrl)));
        }

        return (
            Encoding.UTF8.GetBytes(builder.ToString()),
            "text/csv",
            $"{safeTitle}.csv");
    }

    public async Task<long?> ImportDeckAsync(
        long userId,
        DeckImportRequest request,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (file.Length == 0)
        {
            return null;
        }

        var deckId = await CreateDeckAsync(userId, new CreateDeckRequest
        {
            Title = request.Title,
            Description = request.Description,
            Tags = request.Tags,
            IsPublic = request.IsPublic
        }, cancellationToken);

        IReadOnlyList<CreateVocabularyRequest> rows;
        await using var stream = file.OpenReadStream();
        var extension = Path.GetExtension(file.FileName);

        if (string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            rows = ReadExcelRows(stream);
        }
        else if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            rows = ReadCsvRows(await reader.ReadToEndAsync(cancellationToken));
        }
        else
        {
            return null;
        }

        foreach (var row in rows.Where(row => !string.IsNullOrWhiteSpace(row.Word)))
        {
            await CreateVocabularyAsync(userId, deckId, row, cancellationToken);
        }

        return deckId;
    }

    private static async Task<DeckDetail?> GetDeckInfoAsync(
        Microsoft.Data.SqlClient.SqlConnection connection,
        long userId,
        long deckId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            DECLARE @Today DATE = CAST(SYSDATETIME() AS DATE);

            SELECT
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
                 WHERE lp.user_id = @userId
                   AND v.deck_id = d.id
                   AND lp.next_review_date IS NOT NULL
                   AND lp.next_review_date <= @Today) AS due_words
            FROM dbo.Decks d
            WHERE d.id = @deckId
              AND d.user_id = @userId;
            """;
        command.AddParameter("@deckId", deckId);
        command.AddParameter("@userId", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new DeckDetail(
            reader.GetInt64(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("title")),
            reader.GetNullableString("description"),
            reader.GetNullableString("tags"),
            reader.GetBoolean(reader.GetOrdinal("is_public")),
            reader.GetDateTime(reader.GetOrdinal("created_at")),
            reader.GetInt32(reader.GetOrdinal("total_words")),
            reader.GetInt32(reader.GetOrdinal("learned_words")),
            reader.GetInt32(reader.GetOrdinal("due_words")),
            Array.Empty<VocabularyItem>(),
            new PaginationInfo(1, 10, 0));
    }

    private static async Task<(IReadOnlyList<VocabularyItem> Items, int TotalItems)> GetVocabularyItemsAsync(
        Microsoft.Data.SqlClient.SqlConnection connection,
        long userId,
        long deckId,
        string? search,
        string? status,
        string? sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Max(pageSize, 1);

        var filters = new List<string> { "d.user_id = @userId", "d.id = @deckId" };
        var orderBy = ResolveVocabularySort(sort);
        var offset = Math.Max(page - 1, 0) * pageSize;

        var countCommand = connection.CreateCommand();
        var listCommand = connection.CreateCommand();

        if (!string.IsNullOrWhiteSpace(search))
        {
            filters.Add("(v.word LIKE @search OR v.meaning_vi LIKE @search OR v.description_en LIKE @search OR v.related_words LIKE @search)");
            countCommand.AddParameter("@search", $"%{search.Trim()}%");
            listCommand.AddParameter("@search", $"%{search.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            switch (status.Trim().ToLowerInvariant())
            {
                case "new":
                case "chuahoc":
                case "chua-hoc":
                    filters.Add("lp.id IS NULL");
                    break;

                case "learning":
                case "danghoc":
                case "dang-hoc":
                    filters.Add("COALESCE(lp.status, N'new') = N'learning'");
                    break;

                case "reviewing":
                case "canon":
                case "can-on":
                    filters.Add("(COALESCE(lp.status, N'new') = N'reviewing' OR (lp.next_review_date IS NOT NULL AND lp.next_review_date <= CAST(SYSDATETIME() AS DATE)))");
                    break;

                case "mastered":
                case "thanhthao":
                case "thanh-thao":
                    filters.Add("COALESCE(lp.status, N'new') = N'mastered'");
                    break;
            }
        }

        countCommand.CommandText =
            $"""
            SELECT COUNT(*)
            FROM dbo.Vocabularies v
            INNER JOIN dbo.Decks d ON d.id = v.deck_id
            LEFT JOIN dbo.Learning_Progress lp ON lp.vocabulary_id = v.id AND lp.user_id = @userId
            WHERE {string.Join(" AND ", filters)};
            """;
        countCommand.AddParameter("@userId", userId);
        countCommand.AddParameter("@deckId", deckId);

        var totalItems = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        listCommand.CommandText =
            $"""
            SELECT
                v.id,
                v.word,
                v.pronunciation,
                v.pos,
                v.meaning_vi,
                v.description_en,
                v.example_sentence,
                v.collocations,
                v.related_words,
                v.note,
                v.image_url,
                v.audio_url,
                COALESCE(lp.status, N'new') AS status,
                COALESCE(lp.interval, 0) AS interval,
                COALESCE(lp.ease_factor, 2.5) AS ease_factor,
                COALESCE(lp.repetitions, 0) AS repetitions,
                lp.next_review_date,
                v.created_at
            FROM dbo.Vocabularies v
            INNER JOIN dbo.Decks d ON d.id = v.deck_id
            LEFT JOIN dbo.Learning_Progress lp ON lp.vocabulary_id = v.id AND lp.user_id = @userId
            WHERE {string.Join(" AND ", filters)}
            ORDER BY {orderBy}
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
            """;
        listCommand.AddParameter("@userId", userId);
        listCommand.AddParameter("@deckId", deckId);
        listCommand.AddParameter("@offset", offset);
        listCommand.AddParameter("@pageSize", pageSize);

        var items = new List<VocabularyItem>();
        await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new VocabularyItem(
                reader.GetInt64(reader.GetOrdinal("id")),
                reader.GetString(reader.GetOrdinal("word")),
                reader.GetNullableString("pronunciation"),
                reader.GetNullableInt32("pos"),
                reader.GetNullableString("meaning_vi"),
                reader.GetNullableString("description_en"),
                reader.GetNullableString("example_sentence"),
                reader.GetNullableString("collocations"),
                reader.GetNullableString("related_words"),
                reader.GetNullableString("note"),
                reader.GetNullableString("image_url"),
                reader.GetNullableString("audio_url"),
                reader.GetString(reader.GetOrdinal("status")),
                reader.GetInt32(reader.GetOrdinal("interval")),
                reader.GetDouble(reader.GetOrdinal("ease_factor")),
                reader.GetInt32(reader.GetOrdinal("repetitions")),
                reader.GetNullableDateTime("next_review_date"),
                reader.GetDateTime(reader.GetOrdinal("created_at"))));
        }

        return (items, totalItems);
    }

    private static void AddVocabularyParameters(
        Microsoft.Data.SqlClient.SqlCommand command,
        long deckId,
        CreateVocabularyRequest request,
        string word,
        bool includeDeckId = true)
    {
        if (includeDeckId)
        {
            command.AddParameter("@deckId", deckId);
        }

        command.AddParameter("@word", word);
        command.AddParameter("@pronunciation", NormalizeNullable(request.Pronunciation));
        command.AddParameter("@partOfSpeech", request.PartOfSpeech);
        command.AddParameter("@meaningVi", NormalizeNullable(request.MeaningVi));
        command.AddParameter("@descriptionEn", NormalizeNullable(request.DescriptionEn));
        command.AddParameter("@exampleSentence", NormalizeNullable(request.ExampleSentence));
        command.AddParameter("@collocations", NormalizeNullable(request.Collocations));
        command.AddParameter("@relatedWords", NormalizeNullable(request.RelatedWords));
        command.AddParameter("@note", NormalizeNullable(request.Note));
        command.AddParameter("@imageUrl", NormalizeNullable(request.ImageUrl));
        command.AddParameter("@audioUrl", NormalizeNullable(request.AudioUrl));
    }

    private static async Task<bool> DeckExistsAsync(
        Microsoft.Data.SqlClient.SqlConnection connection,
        long userId,
        long deckId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dbo.Decks WHERE id = @deckId AND user_id = @userId;";
        command.AddParameter("@deckId", deckId);
        command.AddParameter("@userId", userId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static string ResolveDeckSort(string? sort) =>
        sort?.Trim().ToLowerInvariant() switch
        {
            "oldest" => "d.created_at ASC",
            "az" => "d.title ASC",
            "za" => "d.title DESC",
            "progress" => "(SELECT COUNT(*) FROM dbo.Learning_Progress lp INNER JOIN dbo.Vocabularies v ON v.id = lp.vocabulary_id WHERE lp.user_id = @userId AND v.deck_id = d.id) DESC, d.updated_at DESC",
            _ => "d.updated_at DESC, d.created_at DESC"
        };

    private static string ResolveVocabularySort(string? sort) =>
        sort?.Trim().ToLowerInvariant() switch
        {
            "za" => "v.word DESC",
            "newest" => "v.created_at DESC",
            "oldest" => "v.created_at ASC",
            _ => "v.word ASC"
        };

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeRequired(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void WriteExportHeaders(IXLWorksheet worksheet)
    {
        worksheet.Cell(1, 1).Value = "Word";
        worksheet.Cell(1, 2).Value = "Pronunciation";
        worksheet.Cell(1, 3).Value = "PartOfSpeech";
        worksheet.Cell(1, 4).Value = "MeaningVi";
        worksheet.Cell(1, 5).Value = "DescriptionEn";
        worksheet.Cell(1, 6).Value = "ExampleSentence";
        worksheet.Cell(1, 7).Value = "Collocations";
        worksheet.Cell(1, 8).Value = "RelatedWords";
        worksheet.Cell(1, 9).Value = "Note";
        worksheet.Cell(1, 10).Value = "ImageUrl";
        worksheet.Cell(1, 11).Value = "AudioUrl";
        worksheet.Range(1, 1, 1, 11).Style.Font.Bold = true;
    }

    private static void WriteExportRow(IXLWorksheet worksheet, int row, VocabularyItem item)
    {
        worksheet.Cell(row, 1).Value = item.Word;
        worksheet.Cell(row, 2).Value = item.Pronunciation;
        worksheet.Cell(row, 3).Value = item.PartOfSpeech;
        worksheet.Cell(row, 4).Value = item.MeaningVi;
        worksheet.Cell(row, 5).Value = item.DescriptionEn;
        worksheet.Cell(row, 6).Value = item.ExampleSentence;
        worksheet.Cell(row, 7).Value = item.Collocations;
        worksheet.Cell(row, 8).Value = item.RelatedWords;
        worksheet.Cell(row, 9).Value = item.Note;
        worksheet.Cell(row, 10).Value = item.ImageUrl;
        worksheet.Cell(row, 11).Value = item.AudioUrl;
    }

    private static IReadOnlyList<CreateVocabularyRequest> ReadExcelRows(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.First();
        var rows = new List<CreateVocabularyRequest>();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            rows.Add(new CreateVocabularyRequest
            {
                Word = row.Cell(1).GetString(),
                Pronunciation = NullIfEmpty(row.Cell(2).GetString()),
                PartOfSpeech = ParseNullableInt(row.Cell(3).GetString()),
                MeaningVi = NullIfEmpty(row.Cell(4).GetString()),
                DescriptionEn = NullIfEmpty(row.Cell(5).GetString()),
                ExampleSentence = NullIfEmpty(row.Cell(6).GetString()),
                Collocations = NullIfEmpty(row.Cell(7).GetString()),
                RelatedWords = NullIfEmpty(row.Cell(8).GetString()),
                Note = NullIfEmpty(row.Cell(9).GetString()),
                ImageUrl = NullIfEmpty(row.Cell(10).GetString()),
                AudioUrl = NullIfEmpty(row.Cell(11).GetString())
            });
        }

        return rows;
    }

    private static IReadOnlyList<CreateVocabularyRequest> ReadCsvRows(string csvText)
    {
        var rows = new List<CreateVocabularyRequest>();
        var lines = csvText
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Skip(1);

        foreach (var line in lines)
        {
            var columns = ParseCsvLine(line);
            if (columns.Count == 0)
            {
                continue;
            }

            rows.Add(new CreateVocabularyRequest
            {
                Word = columns.ElementAtOrDefault(0) ?? string.Empty,
                Pronunciation = NullIfEmpty(columns.ElementAtOrDefault(1)),
                PartOfSpeech = ParseNullableInt(columns.ElementAtOrDefault(2)),
                MeaningVi = NullIfEmpty(columns.ElementAtOrDefault(3)),
                DescriptionEn = NullIfEmpty(columns.ElementAtOrDefault(4)),
                ExampleSentence = NullIfEmpty(columns.ElementAtOrDefault(5)),
                Collocations = NullIfEmpty(columns.ElementAtOrDefault(6)),
                RelatedWords = NullIfEmpty(columns.ElementAtOrDefault(7)),
                Note = NullIfEmpty(columns.ElementAtOrDefault(8)),
                ImageUrl = NullIfEmpty(columns.ElementAtOrDefault(9)),
                AudioUrl = NullIfEmpty(columns.ElementAtOrDefault(10))
            });
        }

        return rows;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var results = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                results.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        results.Add(current.ToString());
        return results;
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    private static int? ParseNullableInt(string? rawValue) =>
        int.TryParse(rawValue, out var parsed) ? parsed : null;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}