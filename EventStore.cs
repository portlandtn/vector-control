using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

public sealed class EventStore(IOptions<NotifierOptions> options, TimeProvider clock, ILogger<EventStore> logger)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
            Environment.GetEnvironmentVariable("VECTOR_NOTIFIER_DB") ?? "data/notifier.db")),
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared
    }.ToString();
    private readonly NotifierOptions _options = options.Value;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var builder = new SqliteConnectionStringBuilder(_connectionString);
        Directory.CreateDirectory(Path.GetDirectoryName(builder.DataSource)!);
        await using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(ct);
        await ExecAsync(db, """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS events (
              id INTEGER PRIMARY KEY AUTOINCREMENT, dedupe_key TEXT NOT NULL UNIQUE,
              source TEXT NOT NULL, event_type TEXT NOT NULL, severity TEXT NOT NULL,
              emotion TEXT NOT NULL, message TEXT NOT NULL, state_key TEXT, state TEXT,
              occurred_at TEXT, expires_at TEXT, created_at TEXT NOT NULL,
              status TEXT NOT NULL, result TEXT);
            CREATE INDEX IF NOT EXISTS ix_events_status ON events(status, id);
            CREATE TABLE IF NOT EXISTS states (
              state_key TEXT PRIMARY KEY, state TEXT NOT NULL, updated_at TEXT NOT NULL);
            """, ct);
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try { await using var db = await OpenAsync(ct); await ExecAsync(db, "SELECT 1", ct); return true; }
        catch (Exception ex) { logger.LogWarning(ex, "Persistent storage health check failed"); return false; }
    }

    public async Task<EventDecision> AddAsync(NotificationEventRequest e, string initialStatus, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        if (initialStatus == "queued_quiet")
        {
            await using var count = db.CreateCommand(); count.Transaction = (SqliteTransaction)tx;
            count.CommandText = "SELECT COUNT(*) FROM events WHERE status='queued_quiet' AND (expires_at IS NULL OR expires_at > $now)";
            Add(count,"$now",clock.GetUtcNow().ToString("O"));
            if ((long)(await count.ExecuteScalarAsync(ct))! >= _options.MaximumQueuedNotifications)
            { await tx.RollbackAsync(ct); return new(false,"suppressed","quiet_queue_full"); }
        }
        if (!string.IsNullOrWhiteSpace(e.StateKey) && !string.IsNullOrWhiteSpace(e.State))
        {
            await using var state = db.CreateCommand();
            state.Transaction = (SqliteTransaction)tx;
            state.CommandText = "SELECT state FROM states WHERE state_key=$key";
            state.Parameters.AddWithValue("$key", e.StateKey);
            var previous = (string?)await state.ExecuteScalarAsync(ct);
            if (previous is null && e.State.Equals("healthy",StringComparison.OrdinalIgnoreCase))
            {
                await using var baseline = db.CreateCommand(); baseline.Transaction = (SqliteTransaction)tx;
                baseline.CommandText = "INSERT INTO states VALUES($key,$state,$at)";
                Add(baseline,"$key",e.StateKey); Add(baseline,"$state",e.State); Add(baseline,"$at",clock.GetUtcNow().ToString("O"));
                await baseline.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
                return new(false,"suppressed","initial_healthy_state");
            }
            if (previous is not null && string.Equals(previous, e.State, StringComparison.OrdinalIgnoreCase))
            {
                await tx.RollbackAsync(ct);
                return new(false, "suppressed", "unchanged_state");
            }
        }

        await using var cmd = db.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = """
            INSERT INTO events(dedupe_key,source,event_type,severity,emotion,message,state_key,state,occurred_at,expires_at,created_at,status)
            VALUES($dedupe,$source,$type,$severity,$emotion,$message,$stateKey,$state,$occurred,$expires,$created,$status);
            SELECT last_insert_rowid();
            """;
        Add(cmd, "$dedupe", e.DedupeKey!); Add(cmd, "$source", e.Source!); Add(cmd, "$type", e.EventType!);
        Add(cmd, "$severity", e.Severity!); Add(cmd, "$emotion", e.Emotion!); Add(cmd, "$message", e.Message!);
        Add(cmd, "$stateKey", e.StateKey); Add(cmd, "$state", e.State);
        Add(cmd, "$occurred", e.OccurredAt?.ToString("O")); Add(cmd, "$expires", e.ExpiresAt?.ToString("O"));
        Add(cmd, "$created", clock.GetUtcNow().ToString("O")); Add(cmd, "$status", initialStatus);
        long id;
        try { id = (long)(await cmd.ExecuteScalarAsync(ct))!; }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { await tx.RollbackAsync(ct); return new(false, "suppressed", "duplicate"); }

        if (!string.IsNullOrWhiteSpace(e.StateKey) && !string.IsNullOrWhiteSpace(e.State))
        {
            await using var update = db.CreateCommand(); update.Transaction = (SqliteTransaction)tx;
            update.CommandText = "INSERT INTO states VALUES($key,$state,$at) ON CONFLICT(state_key) DO UPDATE SET state=$state,updated_at=$at";
            Add(update, "$key", e.StateKey); Add(update, "$state", e.State); Add(update, "$at", clock.GetUtcNow().ToString("O"));
            await update.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new(true, initialStatus == "queued_quiet" ? "queued" : "accepted", initialStatus == "queued_quiet" ? "quiet_hours" : null, id);
    }

    public async Task<NotificationEnvelope?> ClaimNextAsync(bool includeQuiet, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct); await using var tx = await db.BeginTransactionAsync(ct);
        await using var cmd = db.CreateCommand(); cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = $"SELECT id,source,event_type,severity,emotion,message,dedupe_key,state_key,state,created_at FROM events WHERE status IN ('pending'{(includeQuiet ? ",'queued_quiet'" : "")}) AND (expires_at IS NULL OR expires_at > $now) ORDER BY id LIMIT 1";
        Add(cmd, "$now", clock.GetUtcNow().ToString("O"));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) { await tx.CommitAsync(ct); return null; }
        var item = new NotificationEnvelope(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.IsDBNull(7)?null:reader.GetString(7),reader.IsDBNull(8)?null:reader.GetString(8),DateTimeOffset.Parse(reader.GetString(9)));
        await reader.DisposeAsync();
        await using var claim = db.CreateCommand(); claim.Transaction=(SqliteTransaction)tx; claim.CommandText="UPDATE events SET status='delivering' WHERE id=$id"; Add(claim,"$id",item.Id); await claim.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct); return item;
    }

    public async Task CompleteAsync(long id, DeliveryResult result, CancellationToken ct)
    { await using var db=await OpenAsync(ct); await using var cmd=db.CreateCommand(); cmd.CommandText="UPDATE events SET status=$status,result=$result WHERE id=$id"; Add(cmd,"$status",result.Success?"delivered":"pending"); Add(cmd,"$result",result.Success?"ok":"vector_unavailable_queued"); Add(cmd,"$id",id); await cmd.ExecuteNonQueryAsync(ct); }

    public async Task CleanupAsync(CancellationToken ct)
    { await using var db=await OpenAsync(ct); await using var cmd=db.CreateCommand(); cmd.CommandText="DELETE FROM events WHERE created_at < $events; DELETE FROM states WHERE updated_at < $states; UPDATE events SET status='expired' WHERE status IN ('pending','queued_quiet') AND expires_at <= $now"; Add(cmd,"$events",clock.GetUtcNow().AddHours(-_options.DedupeRetentionHours).ToString("O")); Add(cmd,"$states",clock.GetUtcNow().AddDays(-_options.StateRetentionDays).ToString("O")); Add(cmd,"$now",clock.GetUtcNow().ToString("O")); await cmd.ExecuteNonQueryAsync(ct); }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct) { var db=new SqliteConnection(_connectionString); await db.OpenAsync(ct); return db; }
    private static async Task ExecAsync(SqliteConnection db,string sql,CancellationToken ct) { await using var cmd=db.CreateCommand(); cmd.CommandText=sql; await cmd.ExecuteNonQueryAsync(ct); }
    private static void Add(SqliteCommand cmd,string name,object? value)=>cmd.Parameters.AddWithValue(name,value??DBNull.Value);
}
