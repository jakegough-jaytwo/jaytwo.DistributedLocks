using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
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
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Parameter name is required.", nameof(name));
        }

        var p = command.CreateParameter();
        p.ParameterName = name;
        extraConfig?.Invoke(p);

        // If caller didn’t set Value and this is an input, use DBNull.Value to be safe
        if (p.Direction is ParameterDirection.Input or ParameterDirection.InputOutput && p.Value is null)
        {
            p.Value = DBNull.Value;
        }

        command.Parameters.Add(p);
        return command;
    }

    public static DbCommand WithParameter(this DbCommand command, string name, object? value)
        => WithParameter(command, name, value, dbType: null, direction: null, size: null, precision: null, scale: null);

    public static DbCommand WithParameter(this DbCommand command, string name, object? value, DbType dbType)
        => WithParameter(command, name, value, dbType, direction: null, size: null, precision: null, scale: null);

    public static DbCommand WithParameter(this DbCommand command, string name, object? value, DbType dbType, int size)
        => WithParameter(command, name, value, dbType, direction: null, size: size, precision: null, scale: null);

    public static DbCommand WithParameter(this DbCommand command, string name, object? value = null, DbType? dbType = null, ParameterDirection? direction = null, int? size = null, byte? precision = null, byte? scale = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Parameter name is required.", nameof(name));
        }

        var p = command.CreateParameter();
        p.ParameterName = name;

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

        // Only set Value when provided; otherwise set safe default for inputs
        if (value != null)
        {
            p.Value = value;
        }
        else if (p.Direction is ParameterDirection.Input or ParameterDirection.InputOutput)
        {
            p.Value = DBNull.Value;
        }

        command.Parameters.Add(p);

        return command;
    }

    public static T? ExecuteScalar<T>(this DbCommand command)
        => ConvertTo<T>(command.ExecuteScalar());

    public static async Task<T?> ExecuteScalarAsync<T>(this DbCommand command, CancellationToken cancellationToken = default)
        => ConvertTo<T>(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

    private static T? ConvertTo<T>(object? value)
    {
        if (value is null || value is DBNull)
        {
            return default;
        }

        if (value is T t)
        {
            return t;
        }

        var targetType = typeof(T);

        var underlying = Nullable.GetUnderlyingType(targetType);
        if (underlying is not null)
        {
            return (T)ChangeTypeCore(value, underlying);
        }

        // Common special-cases that ChangeType doesn't cover well
        if (targetType.IsEnum)
        {
            if (value is string s)
            {
                return (T)Enum.Parse(targetType, s, ignoreCase: true);
            }

            return (T)Enum.ToObject(targetType, System.Convert.ChangeType(value, Enum.GetUnderlyingType(targetType), CultureInfo.InvariantCulture)!);
        }

        if (targetType == typeof(Guid))
        {
            return (T)(object)(value switch
            {
                Guid g => g,
                byte[] bytes => new Guid(bytes),
                string s => Guid.Parse(s),
                _ => new Guid(System.Convert.ChangeType(value, typeof(string), CultureInfo.InvariantCulture)!.ToString()!),
            });
        }

        if (targetType == typeof(TimeSpan))
        {
            return (T)(object)(value switch
            {
                TimeSpan ts => ts,
                string s => TimeSpan.Parse(s, CultureInfo.InvariantCulture),
                long ticks => new TimeSpan(ticks),
                _ => TimeSpan.FromTicks(System.Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            });
        }

        if (targetType == typeof(byte[]) && value is byte[] buf)
        {
            return (T)(object)buf; // no copy; caller beware if mutability matters
        }

        // Fallback – invariant culture
        return (T)ChangeTypeCore(value, targetType);
    }

    private static object ChangeTypeCore(object value, Type targetType)
        => Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture)!;
}
