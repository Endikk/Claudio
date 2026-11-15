using System.Runtime.InteropServices;
using Claudio.Core.Services;

namespace Claudio.App;

/// <summary>Claudy's <c>exportUsage</c>: the last seven days of usage, as a CSV file the user picks.</summary>
internal sealed partial class UsageViewModel
{
    private const uint MessageBoxWarning = 0x30;
    private const uint MessageBoxInformation = 0x40;

    /// <summary>The configuration folder this reading follows: the sessions are read from it too.</summary>
    public ClaudeHome Home => _home;

    /// <summary>
    /// An export is reading the history or waiting on its save dialog. A second click on the menu
    /// item meanwhile is dropped rather than opening a second dialog.
    /// </summary>
    public bool IsExporting { get; private set; }

    /// <summary>
    /// Writes the last seven days of usage to a CSV file the user picks. Local transcripts only, so
    /// a PC without Claude Code has nothing to export, never the demo set.
    /// </summary>
    /// <param name="owner">The window the dialogs belong to.</param>
    public void ExportUsage(nint owner)
    {
        if (IsExporting)
        {
            return;
        }
        IsExporting = true;
        _ = RunExportAsync(owner);
    }

    private async Task RunExportAsync(nint owner)
    {
        try
        {
            var now = DateTimeOffset.Now;
            IReadOnlyList<TranscriptEntry> entries;
            try
            {
                entries = await _source.RecentEntriesAsync();
            }
            catch (UsageDataException)
            {
                Alert(owner, "Could not read the usage history", "The Claude Code transcripts folder could not be read (permissions?).");
                return;
            }
#pragma warning disable CA1031 // Any failure reading the history is told to the user, never thrown.
            catch (Exception error)
#pragma warning restore CA1031
            {
                Alert(owner, "Could not read the usage history", error.Message);
                return;
            }

            // A busy week holds a great many responses, each formatted into a date: the file is
            // built off the UI thread so the card keeps drawing meanwhile.
            var csv = await Task.Run(() =>
            {
                var rows = UsageExport.Rows(entries, now, TimeZoneInfo.Local);
                return rows.Count == 0 ? null : UsageExport.Csv(rows);
            });
            if (csv is null)
            {
                Alert(owner, "No usage to export", "Claude Code recorded no usage on this PC over the last 7 days.", MessageBoxInformation);
                return;
            }

            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedFileName = Path.GetFileNameWithoutExtension(UsageExport.FileName(now, TimeZoneInfo.Local)),
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeChoices.Add("CSV", [".csv"]);
            // An unpackaged app hands the picker its window, or the picker has nothing to open over.
            WinRT.Interop.InitializeWithWindow.Initialize(picker, owner);
            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }
            try
            {
                await File.WriteAllTextAsync(file.Path, csv, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
#pragma warning disable CA1031 // A write that fails is told to the user, never thrown.
            catch (Exception error)
#pragma warning restore CA1031
            {
                Alert(owner, "Could not save the usage export", $"Writing {file.Path} failed: {error.Message}");
            }
        }
        finally
        {
            IsExporting = false;
        }
    }

    /// <summary>A plain system alert over Claudio's window, as Claudy's <c>NSAlert</c>.</summary>
    private static void Alert(nint owner, string message, string detail, uint style = MessageBoxWarning) =>
        _ = MessageBoxW(owner, $"{message}\n\n{detail}", "Claudio", style);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint owner, string text, string caption, uint type);
}
