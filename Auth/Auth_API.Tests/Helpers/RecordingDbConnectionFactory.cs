using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Auth.Infrastructure.Persistence;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// Minimal ADO.NET test double that lets Dapper build a real command and
/// parameters while controlling only the affected-row result.
/// </summary>
internal sealed class RecordingDbConnectionFactory(int affectedRows) : IDbConnectionFactory
{
    private readonly List<RecordedCommand> _commands = [];

    public RecordedCommand? LastCommand { get; private set; }

    /// <summary>Every command issued, in order — for methods that issue several.</summary>
    public IReadOnlyList<RecordedCommand> Commands => _commands;

    /// <summary>The most recent transaction a connection handed out, if any.</summary>
    public RecordingDbTransaction? LastTransaction { get; private set; }

    public Task<IDbConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IDbConnection>(new RecordingDbConnection(
            affectedRows,
            command => { _commands.Add(command); LastCommand = command; },
            transaction => LastTransaction = transaction));
    }
}

/// <param name="InTransaction">
/// Whether the command carried a transaction. Dapper only enlists a command in
/// a transaction when the caller passes it explicitly, and SqlClient throws for
/// a command issued on a connection with an open transaction it was not given
/// — so a transactional method is only correct if every one of its commands
/// records true here.
/// </param>
internal sealed record RecordedCommand(
    string CommandText,
    IReadOnlyDictionary<string, object?> Parameters,
    bool InTransaction = false);

/// <summary>
/// A transaction that does nothing but remember how it ended. The double never
/// simulates a database; it lets a method that opens a transaction run to its
/// end so the commands it issued inside can be asserted on.
/// </summary>
internal sealed class RecordingDbTransaction(DbConnection connection, IsolationLevel isolationLevel) : DbTransaction
{
    public bool Committed { get; private set; }
    public bool RolledBack { get; private set; }

    public override IsolationLevel IsolationLevel => isolationLevel;
    protected override DbConnection? DbConnection => connection;

    public override void Commit() => Committed = true;
    public override void Rollback() => RolledBack = true;
}

internal sealed class RecordingDbConnection(
    int affectedRows,
    Action<RecordedCommand> record,
    Action<RecordingDbTransaction>? onTransaction = null) : DbConnection
{
    private ConnectionState _state = ConnectionState.Open;

    [AllowNull]
    public override string ConnectionString { get; set; } = string.Empty;
    public override string Database => "Recording";
    public override string DataSource => "Recording";
    public override string ServerVersion => "1";
    public override ConnectionState State => _state;

    public override void ChangeDatabase(string databaseName) { }
    public override void Close() => _state = ConnectionState.Closed;
    public override void Open() => _state = ConnectionState.Open;
    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Open();
        return Task.CompletedTask;
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        var transaction = new RecordingDbTransaction(this, isolationLevel);
        onTransaction?.Invoke(transaction);
        return transaction;
    }

    protected override DbCommand CreateDbCommand() =>
        new RecordingDbCommand(this, affectedRows, record);
}

internal sealed class RecordingDbCommand(
    DbConnection connection,
    int affectedRows,
    Action<RecordedCommand> record) : DbCommand
{
    private readonly RecordingDbParameterCollection _parameters = new();

    [AllowNull]
    public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; } = connection;
    protected override DbParameterCollection DbParameterCollection => _parameters;
    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel() { }
    public override int ExecuteNonQuery() => Execute();
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => new RecordingDbParameter();

    // Scalar and reader are supported so that a read path can be recorded too,
    // not only a write. A paged query runs its COUNT first and then the page, so
    // both have to answer something for the method to reach its end — and
    // LastCommand is the page, which is the one carrying the WHERE clause worth
    // asserting on. Both return emptiness: this double exists to capture the SQL
    // Dapper builds, never to simulate a database.
    public override object? ExecuteScalar() => Execute();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        Execute();
        return new DataTableReader(new DataTable());
    }

    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Execute());
    }

    private int Execute()
    {
        record(new RecordedCommand(
            CommandText,
            _parameters.Cast<DbParameter>().ToDictionary(
                parameter => parameter.ParameterName.TrimStart('@'),
                parameter => parameter.Value is DBNull ? null : parameter.Value,
                StringComparer.OrdinalIgnoreCase),
            InTransaction: DbTransaction is not null));
        return affectedRows;
    }
}

internal sealed class RecordingDbParameter : DbParameter
{
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
    public override bool IsNullable { get; set; }
    [AllowNull]
    public override string ParameterName { get; set; } = string.Empty;
    public override int Size { get; set; }
    [AllowNull]
    public override string SourceColumn { get; set; } = string.Empty;
    public override bool SourceColumnNullMapping { get; set; }
    public override object? Value { get; set; }
    public override void ResetDbType() { }
}

internal sealed class RecordingDbParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _items = [];

    public override int Count => _items.Count;
    public override object SyncRoot => ((ICollection)_items).SyncRoot;
    public override int Add(object value)
    {
        _items.Add((DbParameter)value);
        return _items.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (var value in values) Add(value!);
    }

    public override void Clear() => _items.Clear();
    public override bool Contains(object value) => _items.Contains((DbParameter)value);
    public override bool Contains(string value) => IndexOf(value) >= 0;
    public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public override IEnumerator GetEnumerator() => _items.GetEnumerator();
    public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
    public override int IndexOf(string parameterName) =>
        _items.FindIndex(item => string.Equals(
            item.ParameterName.TrimStart('@'),
            parameterName.TrimStart('@'),
            StringComparison.OrdinalIgnoreCase));

    public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
    public override void Remove(object value) => _items.Remove((DbParameter)value);
    public override void RemoveAt(int index) => _items.RemoveAt(index);
    public override void RemoveAt(string parameterName)
    {
        var index = IndexOf(parameterName);
        if (index >= 0) RemoveAt(index);
    }

    protected override DbParameter GetParameter(int index) => _items[index];
    protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
    protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value)
    {
        var index = IndexOf(parameterName);
        if (index >= 0) _items[index] = value;
        else _items.Add(value);
    }
}
