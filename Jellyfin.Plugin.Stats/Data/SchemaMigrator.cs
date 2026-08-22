using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// Takes a store from whatever version it is at to the newest one a list of
/// steps knows, and refuses one that is already past that.
/// </summary>
/// <remarks>
/// The version lives in a table of the store's own rather than in the file
/// header pragma SQLite offers for it. A pragma takes no parameter, so writing
/// one means building a statement out of a number, and this plugin's rule is
/// that a statement is a constant and a value is a parameter. A row costs one
/// table and keeps that rule whole.
/// <para>
/// The step list is an argument rather than a static this reaches for, so a test
/// drives the runner over a list of its own. A runner exercised only through the
/// real list could not be tested at all until there were two real versions,
/// which is one version after the first time it matters.
/// </para>
/// </remarks>
public static class SchemaMigrator
{
    private const string CreateTheVersionTable =
        "CREATE TABLE IF NOT EXISTS schema_version (Version INTEGER NOT NULL)";

    private const string ReadTheVersion =
        @"-- bound: LIMIT 1, over a table that holds one row
          SELECT Version FROM schema_version LIMIT 1";

    private const string ForgetTheVersion =
        "DELETE FROM schema_version";

    private const string RecordTheVersion =
        "INSERT INTO schema_version (Version) VALUES ($version)";

    /// <summary>
    /// Applies every step above the store's current version, in order.
    /// </summary>
    /// <remarks>
    /// Each step and the version it produces are committed together. A process
    /// that dies half way through leaves the store at the version it was at
    /// with the rows it had, rather than at a version whose shape it does not
    /// have.
    /// </remarks>
    /// <param name="connection">An open connection to the store.</param>
    /// <param name="migrations">The steps, in ascending version order.</param>
    /// <returns>The version the store is at afterwards.</returns>
    /// <exception cref="StoreIsNewerThanThePluginException">The store is past the last step in the list.</exception>
    public static int MigrateToLatest(SqliteConnection connection, IReadOnlyList<SchemaMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(migrations);

        // A list with nothing in it has no newest version to compare a store
        // against, so it is a mistake rather than a store that needs nothing
        // doing to it.
        ArgumentOutOfRangeException.ThrowIfZero(migrations.Count);

        Execute(connection, CreateTheVersionTable);

        var current = CurrentVersion(connection);
        var latest = migrations[^1].Version;

        if (current > latest)
        {
            throw new StoreIsNewerThanThePluginException(current, latest);
        }

        foreach (var migration in migrations)
        {
            if (migration.Version <= current)
            {
                continue;
            }

            Apply(connection, migration);
            current = migration.Version;
        }

        return current;
    }

    /// <summary>
    /// Reads the version the store is at.
    /// </summary>
    /// <param name="connection">An open connection to the store.</param>
    /// <returns>The version, and zero for a store no step has run against yet.</returns>
    public static int CurrentVersion(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var command = connection.CreateCommand();
        command.CommandText = ReadTheVersion;

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return reader.GetInt32(0);
        }

        // No row at all is a store that has never been migrated. Zero rather
        // than a null, so every caller compares numbers and none of them has a
        // second case for the first run.
        return 0;
    }

    /// <summary>
    /// Runs one step and records the version it produced, both or neither.
    /// </summary>
    /// <param name="connection">An open connection to the store.</param>
    /// <param name="migration">The step.</param>
    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "A migration statement is not a query the plugin composes at run time. Every one of them is a constant in this assembly, reached through the list in SchemaMigrations, and no route from a request, a configuration value or a stored row arrives here. The analyser cannot see that because the statement is data by design: making the runner testable over a step list a test owns is what puts a variable in front of CommandText, and the alternative is a runner nothing can exercise until there are two shipped versions.")]
    private static void Apply(SqliteConnection connection, SchemaMigration migration)
    {
        using var transaction = connection.BeginTransaction();

        foreach (var statement in migration.Statements)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }

        using (var forget = connection.CreateCommand())
        {
            forget.Transaction = transaction;
            forget.CommandText = ForgetTheVersion;
            forget.ExecuteNonQuery();
        }

        using (var record = connection.CreateCommand())
        {
            record.Transaction = transaction;
            record.CommandText = RecordTheVersion;
            record.Parameters.AddWithValue("$version", migration.Version);
            record.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "The only caller passes a constant declared a few lines above, and the parameter exists so that constant is written once rather than beside every command this file opens.")]
    private static void Execute(SqliteConnection connection, string statement)
    {
        using var command = connection.CreateCommand();
        command.CommandText = statement;
        command.ExecuteNonQuery();
    }
}
