using System;

namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// Thrown when a read could not open the store at all.
/// </summary>
/// <remarks>
/// The failure this names is the file, not the answer. A store that will not
/// open holds rows nobody can see and a store that opens and holds nothing
/// holds no rows, and those are opposite facts about somebody's history. An
/// endpoint that met both as an empty list would tell a user their year was
/// quiet when the truth is that the plugin cannot read it.
/// <para>
/// So the type exists to be caught. It is thrown where a read opens the store
/// and the open fails, it carries what the file system or the migration
/// actually said as its inner exception, and the endpoint above it turns it
/// into a status rather than into an answer. Issue #31 is where that is asked
/// for.
/// </para>
/// <para>
/// It is deliberately narrow. A statement that a store refused once it was open
/// is one read of one table going wrong, and the write path already keeps those
/// two apart for the same reason: caught together they are one number and one
/// class of failure, and the only way left to tell them apart is to read the
/// exception type and know which one it means.
/// </para>
/// </remarks>
public sealed class StoreCouldNotBeOpenedException : InvalidOperationException
{
    private const string WhatHappened = "The store could not be opened.";

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreCouldNotBeOpenedException"/> class.
    /// </summary>
    /// <param name="innerException">What the open actually threw.</param>
    public StoreCouldNotBeOpenedException(Exception innerException)
        : base(WhatHappened, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreCouldNotBeOpenedException"/> class.
    /// </summary>
    public StoreCouldNotBeOpenedException()
        : base(WhatHappened)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreCouldNotBeOpenedException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public StoreCouldNotBeOpenedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreCouldNotBeOpenedException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">What went wrong underneath.</param>
    public StoreCouldNotBeOpenedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
