using Velopack;
using Velopack.Sources;

namespace Claudio.App;

/// <summary>
/// Looks for a newer Claudio among the GitHub releases and downloads it in the background; it is
/// applied when Claudio quits, never under the user's feet. Silent on any failure: no network
/// means no update, never an error. A copy run from a build folder, not installed, does nothing.
/// </summary>
internal static class Updates
{
    private const string Repository = "https://github.com/Endikk/Claudio";

    public static async Task CheckAsync()
    {
        try
        {
            var manager = new UpdateManager(new GithubSource(Repository, accessToken: null, prerelease: false));
            if (!manager.IsInstalled)
            {
                return;
            }
            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                return;
            }
            await manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
            manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: false);
        }
#pragma warning disable CA1031 // An update check must never take Claudio down.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
