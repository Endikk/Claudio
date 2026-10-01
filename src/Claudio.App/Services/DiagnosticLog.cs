namespace Claudio.App;

/// <summary>
/// Timestamped log of API failures at <c>%LOCALAPPDATA%\Claudio\api.log</c>: what tells a rate
/// limit from a dead token. Bounded: past 512 KB it starts over.
/// </summary>
internal static class DiagnosticLog
{
    private static readonly string File = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Claudio", "api.log");

    public static void Append(string message)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(File)!);
            if (new FileInfo(File) is { Exists: true, Length: > 512_000 })
            {
                System.IO.File.Delete(File);
            }
            System.IO.File.AppendAllText(File, $"{DateTimeOffset.Now:yyyy-MM-ddTHH:mm:sszzz} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
