using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Stats;

/// <summary>
/// Removes what this plugin wrote to disk, when the plugin is being removed.
/// </summary>
/// <remarks>
/// The server deletes the folder the assembly sits in and nothing else. The
/// configuration file lives somewhere else entirely, under the server's plugin
/// configuration directory, so an uninstall that leaves it there leaves this
/// plugin's settings on disk after the plugin is gone. Everything this plugin
/// writes for itself lives in its data folder, which the server does not touch
/// either.
/// <para>
/// It is a plain function over three paths rather than a method reaching for
/// the plugin instance, so every branch below is reachable from a test with a
/// temporary directory, including the two that fail.
/// </para>
/// <para>
/// The hook that calls this runs before the uninstall completes, and the
/// uninstall can still fail afterwards. A removal that has already happened is
/// therefore not conditional on the uninstall succeeding, and that direction is
/// deliberate: a plugin that keeps personal detail on disk because its own
/// removal was interrupted is the failure this exists against.
/// </para>
/// </remarks>
public static class PluginDataRemoval
{
    /// <summary>
    /// Deletes the plugin's data folder and its configuration file.
    /// </summary>
    /// <remarks>
    /// A path that is already gone is not an error and is not logged: an
    /// uninstall of a plugin that never wrote anything is the ordinary case.
    /// </remarks>
    /// <param name="dataFolderPath">The plugin's data folder.</param>
    /// <param name="configurationFilePath">The plugin's configuration file, which lives outside that folder.</param>
    /// <param name="assemblyFilePath">Where the plugin assembly was loaded from, which decides whether the data folder is the server's to delete.</param>
    /// <param name="logger">The logger. What could not be deleted is named on it, so an administrator can finish it by hand.</param>
    public static void Remove(string dataFolderPath, string configurationFilePath, string assemblyFilePath, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(dataFolderPath);
        ArgumentNullException.ThrowIfNull(configurationFilePath);
        ArgumentNullException.ThrowIfNull(assemblyFilePath);
        ArgumentNullException.ThrowIfNull(logger);

        if (IsTheFolderTheAssemblyWasLoadedFrom(dataFolderPath, assemblyFilePath))
        {
            // The two are the same directory when the plugin was installed by
            // hand into a folder named after its assembly. Deleting it here
            // would take the assembly and the manifest with it, and the server
            // deletes that same folder immediately afterwards, so the data
            // still goes. What this must not do is fail in the middle and leave
            // the server unable to finish its own removal.
            logger.LogWarning(
                "The plugin's data folder is the folder it was installed into, so it is left for the server's own removal to delete: {DataFolderPath}",
                dataFolderPath);
        }
        else if (Path.Exists(dataFolderPath))
        {
            // Path.Exists rather than Directory.Exists, so a data folder path
            // that turns out to be a file is reported rather than passed over
            // as if there had been nothing there.
            Attempt(() => Directory.Delete(dataFolderPath, true), dataFolderPath, logger);
        }

        // Path.Exists again, and for a second reason as well. File.Exists is
        // false for a directory, and a directory sitting where the
        // configuration file belongs is one of the two ways a deletion here
        // fails; guarding on File.Exists would pass over it in silence. The
        // guard is needed at all because File.Delete forgives a missing file
        // and not a missing directory above it, which is the ordinary state of
        // a server where no plugin has ever saved configuration.
        if (Path.Exists(configurationFilePath))
        {
            Attempt(() => File.Delete(configurationFilePath), configurationFilePath, logger);
        }
    }

    /// <summary>
    /// Runs one deletion and reports what it could not remove.
    /// </summary>
    /// <remarks>
    /// Both deletions come through here, so there is one place that decides
    /// what a failure does and one log line to read. It swallows rather than
    /// throws: this runs inside the server's uninstall, and an exception out of
    /// it would stop the uninstall over a file somebody can delete by hand.
    /// </remarks>
    /// <param name="deletion">The deletion to run.</param>
    /// <param name="path">What is being deleted, for the log line.</param>
    /// <param name="logger">The logger.</param>
    private static void Attempt(Action deletion, string path, ILogger logger)
    {
        try
        {
            deletion();
        }
        catch (IOException ex)
        {
            Report(ex, path, logger);
        }
        catch (UnauthorizedAccessException ex)
        {
            Report(ex, path, logger);
        }
    }

    /// <summary>
    /// Says on the log what was left behind and where it is.
    /// </summary>
    /// <param name="ex">Why the deletion failed.</param>
    /// <param name="path">What is still on disk.</param>
    /// <param name="logger">The logger.</param>
    private static void Report(Exception ex, string path, ILogger logger)
    {
        logger.LogError(
            ex,
            "The plugin's data at {Path} could not be removed while uninstalling. It is still on disk and can be deleted by hand.",
            path);
    }

    /// <summary>
    /// Decides whether the data folder is the folder the assembly was loaded
    /// from.
    /// </summary>
    /// <remarks>
    /// The comparison ignores case on every platform rather than following the
    /// file system's own rule. Being wrong in the ignoring direction leaves a
    /// folder on disk and says so on the log; being wrong in the other
    /// direction deletes the running plugin's own folder underneath the server
    /// mid-uninstall. Where the two cannot both be avoided without asking the
    /// file system a question this is not asking it, the milder failure is the
    /// one taken.
    /// </remarks>
    /// <param name="dataFolderPath">The plugin's data folder.</param>
    /// <param name="assemblyFilePath">Where the plugin assembly was loaded from.</param>
    /// <returns>Whether the two are the same directory.</returns>
    private static bool IsTheFolderTheAssemblyWasLoadedFrom(string dataFolderPath, string assemblyFilePath)
    {
        // Combining with the parent rather than reading the directory name,
        // because the directory name of a root path is null and that null has
        // no reachable case behind it here.
        var assemblyFolder = Path.GetFullPath(Path.Combine(assemblyFilePath, ".."));

        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataFolderPath)),
            Path.TrimEndingDirectorySeparator(assemblyFolder),
            StringComparison.OrdinalIgnoreCase);
    }
}
