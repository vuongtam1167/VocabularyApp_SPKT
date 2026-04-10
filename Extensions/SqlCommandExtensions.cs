using Microsoft.Data.SqlClient;

namespace Vocab_LearningApp.Extensions;

public static class SqlCommandExtensions
{
    public static void AddParameter(this SqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
