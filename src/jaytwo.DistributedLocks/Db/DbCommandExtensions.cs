using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace jaytwo.DistributedLocks.Db;

public static class DbCommandExtensions
{
    public static DbCommand WithCommandText(this DbCommand command, string commandText)
    {
        command.CommandText = commandText;
        return command;
    }

    public static DbCommand WithTransaction(this DbCommand command, DbTransaction transaction)
    {
        command.Transaction = transaction;
        return command;
    }

    public static DbCommand WithCommandTimeout(this DbCommand command, int commandTimeout)
    {
        command.CommandTimeout = commandTimeout;
        return command;
    }

    public static DbCommand WithCommandType(this DbCommand command, CommandType commandType)
    {
        command.CommandType = commandType;
        return command;
    }

    public static DbCommand WithParameter(this DbCommand command, string name, Action<DbParameter>? extraConfig = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        extraConfig?.Invoke(parameter);

        command.Parameters.Add(parameter);
        return command;
    }

    public static DbCommand WithParameter(this DbCommand command, string name, object? value)
        => command.WithParameter(name, p => p.WithValue(value));

    public static DbCommand WithParameter(this DbCommand command, string name, object? value, DbType dbType)
        => command.WithParameter(name, p => p.WithDbType(dbType).WithValue(value));

    public static DbCommand WithParameter(this DbCommand command, string name, object? value, DbType dbType, int size)
        => command.WithParameter(name, p => p.WithDbType(dbType).WithSize(size).WithValue(value));

    public static DbCommand WithParameter(this DbCommand command, string name, object? value = null, DbType? dbType = null, ParameterDirection? direction = null, int? size = null, byte? precision = null, byte? scale = null)
        => command.WithParameter(name, p =>
        {
            if (dbType.HasValue)
            {
                p.DbType = dbType.Value;
            }

            if (direction.HasValue)
            {
                p.Direction = direction.Value;
            }

            if (size.HasValue)
            {
                p.Size = size.Value;
            }

            if (precision.HasValue)
            {
                p.Precision = precision.Value;
            }

            if (scale.HasValue)
            {
                p.Scale = scale.Value;
            }

            if (value != null)
            {
                p.Value = value;
            }
        });

    public static T? ExecuteScalar<T>(this DbCommand command)
        => (T?)command.ExecuteScalar();

    public static async Task<T?> ExecuteScalarAsync<T>(this DbCommand command, CancellationToken cancellationToken = default)
        => (T?)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
}
