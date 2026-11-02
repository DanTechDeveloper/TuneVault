using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TuneVault.Models;

namespace TuneVault.Services;

/// <summary>
/// SQLite implementation of <see cref="ILibraryRepository"/>. The database lives in
/// the per-user application data folder so the app can be installed read-only.
/// </summary>
public sealed class SqliteLibraryRepository : ILibraryRepository
{
    private const int SchemaVersion = 1;

    // SQLITE_CONSTRAINT_PRIMARYKEY / SQLITE_CONSTRAINT_UNIQUE
    private const int PrimaryKeyConstraint = 1555;
    private const int UniqueConstraint = 2067;

    private readonly string _connectionString;

    public SqliteLibraryRepository(string? databasePath = null)
    {
        var path = databasePath ?? DefaultDatabasePath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = true,
            DefaultTimeout = 5,
        }.ToString();
    }

    public static string DefaultDatabasePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TuneVault",
            "library.db");

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS tracks (
            id             TEXT PRIMARY KEY,
            path_key       TEXT NOT NULL UNIQUE,
            identity_key   TEXT NULL UNIQUE,
            file_path      TEXT NOT NULL,
            title          TEXT NOT NULL,
            artist         TEXT NOT NULL,
            album          TEXT,
            genre          TEXT,
            year           INTEGER NOT NULL DEFAULT 0,
            duration_ticks INTEGER NOT NULL DEFAULT 0,
            date_added     TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_tracks_title_artist
            ON tracks (title COLLATE NOCASE, artist COLLATE NOCASE);

        CREATE TABLE IF NOT EXISTS track_art (
            track_id TEXT PRIMARY KEY REFERENCES tracks(id) ON DELETE CASCADE,
            data     BLOB NOT NULL
        );
        """;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);

        try
        {
            await ExecuteAsync(connection, null, "PRAGMA journal_mode=WAL;", cancellationToken);
        }
        catch (SqliteException)
        {
            // Filesystems without WAL support (network shares) fall back to the default journal.
        }

        var version = Convert.ToInt32(
            await ScalarAsync(connection, null, "PRAGMA user_version;", cancellationToken),
            CultureInfo.InvariantCulture);

        if (version >= SchemaVersion)
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        await ExecuteAsync(connection, transaction, SchemaSql, cancellationToken);
        await ExecuteAsync(connection, transaction, $"PRAGMA user_version = {SchemaVersion};", cancellationToken);
        transaction.Commit();
    }

    public async Task<IReadOnlyList<Track>> LoadAllAsync(
        TrackSortField sortBy = TrackSortField.Title,
        bool descending = false,
        CancellationToken cancellationToken = default)
    {
        var tracks = new List<Track>();

        // Column comes from a fixed whitelist, never from user input. CASE keeps
        // unknown values (NULL/empty/0) last in both directions; a bare integer
        // in ORDER BY would be read as a column index.
        var (column, missingLast) = sortBy switch
        {
            TrackSortField.Artist => ("t.artist COLLATE NOCASE", "0"),
            TrackSortField.Album => ("t.album COLLATE NOCASE", "CASE WHEN t.album IS NULL OR t.album = '' THEN 1 ELSE 0 END"),
            TrackSortField.Genre => ("t.genre COLLATE NOCASE", "CASE WHEN t.genre IS NULL OR t.genre = '' THEN 1 ELSE 0 END"),
            TrackSortField.Year => ("t.year", "CASE WHEN t.year = 0 THEN 1 ELSE 0 END"),
            _ => ("t.title COLLATE NOCASE", "0"),
        };
        var direction = descending ? "DESC" : "ASC";
        var missingTerm = missingLast == "0" ? "" : $"{missingLast} ASC, ";

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT t.id, t.file_path, t.title, t.artist,
                   t.album, t.genre, t.year, t.duration_ticks, a.data
            FROM tracks t
            LEFT JOIN track_art a ON a.track_id = t.id
            ORDER BY {missingTerm}{column} {direction},
                     t.title COLLATE NOCASE ASC, t.id ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tracks.Add(new Track
            {
                Id = Guid.Parse(reader.GetString(0)),
                FilePath = reader.GetString(1),
                Title = reader.GetString(2),
                Artist = reader.GetString(3),
                Album = reader.IsDBNull(4) ? null : reader.GetString(4),
                Genre = reader.IsDBNull(5) ? null : reader.GetString(5),
                Year = (uint)reader.GetInt64(6),
                Duration = TimeSpan.FromTicks(reader.GetInt64(7)),
                PictureData = reader.IsDBNull(8) ? null : (byte[])reader.GetValue(8),
            });
        }

        return tracks;
    }

    public async Task<IReadOnlyDictionary<string, string>> LoadPathIndexAsync(
        CancellationToken cancellationToken = default)
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT path_key, title FROM tracks;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            index[reader.GetString(0)] = reader.GetString(1);
        }

        return index;
    }

    public async Task<bool> IdentityExistsAsync(
        string? title,
        string? artist,
        Guid? excludeTrackId = null,
        CancellationToken cancellationToken = default)
    {
        var identityKey = LibraryKeys.ForIdentity(title, artist);
        if (identityKey is null)
        {
            return false;
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = excludeTrackId is null
            ? "SELECT 1 FROM tracks WHERE identity_key = $identity LIMIT 1;"
            : "SELECT 1 FROM tracks WHERE identity_key = $identity AND id <> $id LIMIT 1;";
        command.Parameters.AddWithValue("$identity", identityKey);
        if (excludeTrackId is not null)
        {
            command.Parameters.AddWithValue("$id", excludeTrackId.Value.ToString("D"));
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    public async Task<IReadOnlyList<AddResult>> AddRangeAsync(
        IReadOnlyList<Track> tracks,
        CancellationToken cancellationToken = default)
    {
        var results = new List<AddResult>(tracks.Count);
        if (tracks.Count == 0)
        {
            return results;
        }

        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        foreach (var track in tracks)
        {
            if (track.FilePath is null)
            {
                results.Add(new AddResult(track, AddOutcome.Failed));
                continue;
            }

            var pathKey = LibraryKeys.ForPath(track.FilePath);
            var identityKey = LibraryKeys.ForIdentity(track.Title, track.Artist);

            try
            {
                await InsertAsync(connection, transaction, track, pathKey, identityKey, cancellationToken);
                results.Add(new AddResult(track, AddOutcome.Added));
            }
            catch (SqliteException ex) when (ex.SqliteExtendedErrorCode is PrimaryKeyConstraint or UniqueConstraint)
            {
                results.Add(new AddResult(
                    track,
                    await ClassifyDuplicateAsync(connection, transaction, pathKey, identityKey, cancellationToken)));
            }
            catch (Exception)
            {
                results.Add(new AddResult(track, AddOutcome.Failed));
            }
        }

        transaction.Commit();
        return results;
    }

    public async Task UpdateAsync(Track track, CancellationToken cancellationToken = default)
    {
        var identityKey = LibraryKeys.ForIdentity(track.Title, track.Artist);

        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE tracks
                SET identity_key = $identity, title = $title, artist = $artist,
                    album = $album, genre = $genre, year = $year, duration_ticks = $duration
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", track.Id.ToString("D"));
            command.Parameters.AddWithValue("$identity", (object?)identityKey ?? DBNull.Value);
            command.Parameters.AddWithValue("$title", track.Title);
            command.Parameters.AddWithValue("$artist", track.Artist);
            command.Parameters.AddWithValue("$album", (object?)track.Album ?? DBNull.Value);
            command.Parameters.AddWithValue("$genre", (object?)track.Genre ?? DBNull.Value);
            command.Parameters.AddWithValue("$year", (long)track.Year);
            command.Parameters.AddWithValue("$duration", track.Duration.Ticks);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (track.PictureData is { Length: > 0 } picture)
        {
            await using var artCommand = connection.CreateCommand();
            artCommand.Transaction = transaction;
            artCommand.CommandText = """
                INSERT INTO track_art (track_id, data) VALUES ($id, $data)
                ON CONFLICT(track_id) DO UPDATE SET data = excluded.data;
                """;
            artCommand.Parameters.AddWithValue("$id", track.Id.ToString("D"));
            artCommand.Parameters.AddWithValue("$data", picture);
            await artCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            await using var artCommand = connection.CreateCommand();
            artCommand.Transaction = transaction;
            artCommand.CommandText = "DELETE FROM track_art WHERE track_id = $id;";
            artCommand.Parameters.AddWithValue("$id", track.Id.ToString("D"));
            await artCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM tracks WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Track track,
        string pathKey,
        string? identityKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO tracks
                (id, path_key, identity_key, file_path, title, artist, album, genre,
                 year, duration_ticks, date_added)
            VALUES
                ($id, $pathKey, $identity, $filePath, $title, $artist, $album, $genre,
                 $year, $duration, $dateAdded);
            """;
        command.Parameters.AddWithValue("$id", track.Id.ToString("D"));
        command.Parameters.AddWithValue("$pathKey", pathKey);
        command.Parameters.AddWithValue("$identity", (object?)identityKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$filePath", track.FilePath!);
        command.Parameters.AddWithValue("$title", track.Title);
        command.Parameters.AddWithValue("$artist", track.Artist);
        command.Parameters.AddWithValue("$album", (object?)track.Album ?? DBNull.Value);
        command.Parameters.AddWithValue("$genre", (object?)track.Genre ?? DBNull.Value);
        command.Parameters.AddWithValue("$year", (long)track.Year);
        command.Parameters.AddWithValue("$duration", track.Duration.Ticks);
        command.Parameters.AddWithValue("$dateAdded", DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);

        if (track.PictureData is { Length: > 0 } picture)
        {
            await using var artCommand = connection.CreateCommand();
            artCommand.Transaction = transaction;
            artCommand.CommandText = "INSERT INTO track_art (track_id, data) VALUES ($id, $data);";
            artCommand.Parameters.AddWithValue("$id", track.Id.ToString("D"));
            artCommand.Parameters.AddWithValue("$data", picture);
            await artCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<AddOutcome> ClassifyDuplicateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string pathKey,
        string? identityKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT path_key FROM tracks
            WHERE path_key = $pathKey
               OR (identity_key IS NOT NULL AND identity_key = $identity)
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$pathKey", pathKey);
        command.Parameters.AddWithValue("$identity", (object?)identityKey ?? DBNull.Value);

        var existingPathKey = await command.ExecuteScalarAsync(cancellationToken) as string;
        return string.Equals(existingPathKey, pathKey, StringComparison.Ordinal)
            ? AddOutcome.DuplicatePath
            : AddOutcome.DuplicateIdentity;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<object?> ScalarAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken);
    }
}
