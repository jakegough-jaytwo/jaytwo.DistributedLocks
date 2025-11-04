using System;
using System.Data;
using System.Data.Common;

namespace jaytwo.DistributedLocks.Db;

public static class DbParameterExtensions
{
    public static DbParameter WithValue(this DbParameter parameter, object? value)
    {
        parameter.Value = value ?? DBNull.Value;
        return parameter;
    }

    public static DbParameter WithDBNullValue(this DbParameter parameter)
    {
        parameter.Value = DBNull.Value;
        return parameter;
    }

    public static DbParameter WithDbType(this DbParameter parameter, DbType dbType)
    {
        parameter.DbType = dbType;
        return parameter;
    }

    public static DbParameter WithDirection(this DbParameter parameter, ParameterDirection direction)
    {
        parameter.Direction = direction;
        return parameter;
    }

    public static DbParameter WithSize(this DbParameter parameter, int size)
    {
        parameter.Size = size;
        return parameter;
    }

    public static DbParameter WithScale(this DbParameter parameter, byte scale)
    {
        parameter.Scale = scale;
        return parameter;
    }

    public static DbParameter WithPrecision(this DbParameter parameter, byte precision)
    {
        parameter.Precision = precision;
        return parameter;
    }
}
