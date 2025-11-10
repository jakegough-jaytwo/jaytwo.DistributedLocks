using System;
using System.Collections;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace jaytwo.DistributedLocks.Db;

internal sealed class WrappedDbDataReader : DbDataReader
{
    private readonly DbDataReader _inner;
    private readonly DbCommand _command;
    private bool _disposed;

    public WrappedDbDataReader(DbDataReader inner, DbCommand command)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _command = command ?? throw new ArgumentNullException(nameof(command));
    }

    public DbDataReader Reader => _inner;

    public DbCommand Command => _command;

    public override int FieldCount => _inner.FieldCount;

    public override bool HasRows => _inner.HasRows;

    public override bool IsClosed => _inner.IsClosed;

    public override int Depth => _inner.Depth;

    public override int RecordsAffected => _inner.RecordsAffected;

    public override int VisibleFieldCount => _inner.VisibleFieldCount;

    public override object this[int ordinal] => _inner[ordinal];

    public override object this[string name] => _inner[name];

    public override bool Read() => _inner.Read();

    public override Task<bool> ReadAsync(CancellationToken ct) => _inner.ReadAsync(ct);

    public override bool NextResult() => _inner.NextResult();

    public override Task<bool> NextResultAsync(CancellationToken ct) => _inner.NextResultAsync(ct);

    public override bool GetBoolean(int ordinal) => _inner.GetBoolean(ordinal);

    public override byte GetByte(int ordinal) => _inner.GetByte(ordinal);

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        => _inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);

    public override char GetChar(int ordinal) => _inner.GetChar(ordinal);

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        => _inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);

    public override string GetDataTypeName(int ordinal) => _inner.GetDataTypeName(ordinal);

    public override DateTime GetDateTime(int ordinal) => _inner.GetDateTime(ordinal);

    public override decimal GetDecimal(int ordinal) => _inner.GetDecimal(ordinal);

    public override double GetDouble(int ordinal) => _inner.GetDouble(ordinal);

    public override IEnumerator GetEnumerator() => _inner.GetEnumerator();

#if NET6_0_OR_GREATER
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)]
#endif
    public override Type GetFieldType(int ordinal) => _inner.GetFieldType(ordinal);

    public override float GetFloat(int ordinal) => _inner.GetFloat(ordinal);

    public override Guid GetGuid(int ordinal) => _inner.GetGuid(ordinal);

    public override short GetInt16(int ordinal) => _inner.GetInt16(ordinal);

    public override int GetInt32(int ordinal) => _inner.GetInt32(ordinal);

    public override long GetInt64(int ordinal) => _inner.GetInt64(ordinal);

    public override string GetName(int ordinal) => _inner.GetName(ordinal);

    public override int GetOrdinal(string name) => _inner.GetOrdinal(name);

    public override string GetString(int ordinal) => _inner.GetString(ordinal);

    public override object GetValue(int ordinal) => _inner.GetValue(ordinal);

    public override int GetValues(object[] values) => _inner.GetValues(values);

    public override bool IsDBNull(int ordinal) => _inner.IsDBNull(ordinal);

    public override System.Data.DataTable? GetSchemaTable() => _inner.GetSchemaTable();

    public override Stream GetStream(int ordinal) => _inner.GetStream(ordinal);

    public override TextReader GetTextReader(int ordinal) => _inner.GetTextReader(ordinal);

    public override void Close() => Dispose(true);

    public override Task CloseAsync() => DisposeAsync().AsTask();

    public override async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await _command.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _inner.Dispose();
            _command.Dispose();
        }

        _disposed = true;
        base.Dispose(disposing);
    }
}
