using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.Stats.Privacy;

/// <summary>
/// The words a person is shown before they agree to be named in the views this
/// plugin draws, and the version those words carry.
/// </summary>
/// <remarks>
/// Consent only means anything if what was consented to can be named later, so
/// a stored agreement points at a version rather than at whatever the current
/// text happens to say. That is why the version lives inside the text itself:
/// tying it to the plugin's version would raise it on every release with no
/// word changed, which empties the statement in the record of meaning, and
/// leaving the words in the configuration page would put them in the file most
/// likely to drift, so the record would name a version that no longer holds
/// anything. Decided on issue #42.
/// <para>
/// The text travels in the assembly rather than being read off a path at run
/// time, so what a record points at is what was built and reviewed, and a file
/// substituted beside the plugin changes nothing. The suite reads the same
/// resource and refuses a change to the words that leaves the number where it
/// was, because a version somebody is supposed to remember to raise is a
/// version that will be wrong, and the failure is silent in the worst
/// direction: a person recorded as having agreed to text they were never
/// shown.
/// </para>
/// <para>
/// Nothing reads this yet. The record that points at a version and the endpoint
/// that writes one are the rest of issue #42, and they are held behind a
/// migration step and a controller; this is the piece that depends on neither,
/// and having the words fixed first means whatever cites a version cites one
/// that exists.
/// </para>
/// </remarks>
public static class ConsentWording
{
    /// <summary>
    /// The name the wording is embedded under, which is the root namespace
    /// joined to the path of the file.
    /// </summary>
    public const string ResourceName = "Jellyfin.Plugin.Stats.Privacy.consent.txt";

    private const string VersionPrefix = "Version: ";

    private static readonly string[] Lines = Embedded()
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Split('\n');

    /// <summary>
    /// Gets the version of the wording, as the wording itself declares it.
    /// </summary>
    /// <remarks>
    /// Read out of the first line rather than written beside it in C#, because
    /// two places holding one number is two places for it to be wrong, and the
    /// one a person is shown is the text.
    /// </remarks>
    public static int Version { get; } =
        int.Parse(Lines[0].AsSpan(VersionPrefix.Length), CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets the words themselves, without the line that carries the version.
    /// </summary>
    public static string Text { get; } = string.Join('\n', Lines.Skip(2)).Trim();

    private static string Embedded()
    {
        using var stream = typeof(ConsentWording).Assembly.GetManifestResourceStream(ResourceName)!;
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
