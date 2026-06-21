using Npgsql;
using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed class NpgsqlDatabaseExceptionClassifier : IDatabaseExceptionClassifier
{
    private const string UniqueViolationSqlState = "23505";
    private const string ExclusionViolationSqlState = "23P01";

    public bool IsUniqueConstraintViolation(Exception exception) =>
        exception is PostgresException { SqlState: UniqueViolationSqlState }
        || exception.InnerException is PostgresException { SqlState: UniqueViolationSqlState };

    public bool IsExclusionConstraintViolation(Exception exception) =>
        exception is PostgresException { SqlState: ExclusionViolationSqlState }
        || exception.InnerException is PostgresException { SqlState: ExclusionViolationSqlState };
}
