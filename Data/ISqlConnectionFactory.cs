using Microsoft.Data.SqlClient;

namespace Vocab_LearningApp.Data;

public interface ISqlConnectionFactory
{
    SqlConnection CreateConnection();
}
