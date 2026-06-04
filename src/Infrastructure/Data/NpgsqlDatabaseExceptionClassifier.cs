using Npgsql;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed class NpgsqlDatabaseExceptionClassifier : IDatabaseExceptionClassifier
{
    private const string UniqueViolationSqlState = "23505";

    public bool IsUniqueConstraintViolation(Exception exception) =>
        exception is PostgresException { SqlState: UniqueViolationSqlState }
        || exception.InnerException is PostgresException { SqlState: UniqueViolationSqlState };
}
