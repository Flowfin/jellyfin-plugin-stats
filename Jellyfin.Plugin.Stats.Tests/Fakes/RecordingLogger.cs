// A logger that keeps what it was told, so a test can assert on a message the
// way an administrator reads it: the level it came out at, and the text with
// its values already put in. Asserting on the format string instead would pass
// while the value that makes the line useful was missing.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Stats.Tests.Fakes;

/// <summary>
/// One line the logger was given.
/// </summary>
/// <param name="Level">The level it was written at.</param>
/// <param name="Message">The message, formatted.</param>
public sealed record LoggedLine(LogLevel Level, string Message);

/// <summary>
/// An <see cref="ILogger{TCategoryName}"/> that records rather than writes.
/// </summary>
/// <typeparam name="TCategoryName">The category, which is not used here and exists so this stands in for the typed logger a constructor asks for.</typeparam>
public sealed class RecordingLogger<TCategoryName> : ILogger<TCategoryName>
{
    private readonly List<LoggedLine> _lines = new();

    /// <summary>
    /// Gets what the logger was told, in order.
    /// </summary>
    public IReadOnlyList<LoggedLine> Lines => _lines;

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    /// <inheritdoc />
    /// <remarks>
    /// Every level is enabled. A logger that answered otherwise would let a
    /// test pass because the message was filtered out rather than because it
    /// was not written.
    /// </remarks>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        _lines.Add(new LoggedLine(logLevel, formatter(state, exception)));
    }
}
