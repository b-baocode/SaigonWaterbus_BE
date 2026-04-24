using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed class UserCodeGenerator : IUserCodeGenerator
{
    private readonly ApplicationDbContext _context;

    public UserCodeGenerator(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<string> GenerateNextCodeAsync(string roleCode, CancellationToken cancellationToken)
    {
        var prefix = UserCodes.GetPrefixForRoleCode(roleCode);
        var sequenceName = UserCodes.GetSequenceName(prefix);
        var connection = _context.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT nextval('{sequenceName}')";
            command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();

            var scalar = await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException($"Sequence '{sequenceName}' did not return a value.");

            var nextNumber = Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
            return UserCodes.Format(prefix, nextNumber);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }
}
