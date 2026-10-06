using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claudio.Core.Services;

/// <summary>
/// A small JSON file of settings that can never keep Claudio from starting. Whatever it holds, a
/// read returns a value or the default: a missing file, a damaged one, a key of the wrong type, a
/// number where text was expected. A write goes to a temporary file first and replaces the real one
/// whole, so a crash or a power cut halfway leaves the old settings, never half of the new ones. A
/// file that could not be read is kept beside the real one as <c>.bad</c> rather than overwritten,
/// so what the user had is never lost to a bug.
/// </summary>
public sealed class SettingsStore
{
    private readonly string _path;
    private readonly Action<string>? _log;
    private readonly Lock _gate = new();
    private readonly JsonObject _values;

    public SettingsStore(string path, Action<string>? log = null)
    {
        _path = path;
        _log = log;
        _values = Load();
    }

    public bool Bool(string key, bool fallback)
    {
        lock (_gate)
        {
            return _values[key] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : fallback;
        }
    }

    public string? Text(string key)
    {
        lock (_gate)
        {
            return _values[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        }
    }

    public void Set(string key, bool value) => Write(key, JsonValue.Create(value));

    /// <summary>Null removes the key.</summary>
    public void Set(string key, string? value) => Write(key, value is null ? null : JsonValue.Create(value));

    private void Write(string key, JsonNode? value)
    {
        lock (_gate)
        {
            if (value is null)
            {
                _values.Remove(key);
            }
            else
            {
                _values[key] = value;
            }
            Save();
        }
    }

    private void Save()
    {
        var temporary = _path + ".tmp";
        try
        {
            var folder = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }
            File.WriteAllText(temporary, _values.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(_path))
            {
                File.Replace(temporary, _path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporary, _path);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Read-only disk, antivirus holding the file: the settings hold for this run only.
            _log?.Invoke($"settings not saved: {error.Message}");
            TryDelete(temporary);
        }
    }

    private JsonObject Load()
    {
        string text;
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }
            text = File.ReadAllText(_path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"settings not read: {error.Message}");
            return [];
        }
        try
        {
            if (JsonNode.Parse(text) is JsonObject values)
            {
                return values;
            }
            KeepDamaged("it does not hold an object");
        }
        catch (JsonException error)
        {
            KeepDamaged(error.Message);
        }
        return [];
    }

    private void KeepDamaged(string reason)
    {
        _log?.Invoke($"settings damaged ({reason}): kept as .bad, defaults used");
        try
        {
            File.Copy(_path, _path + ".bad", overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The copy is a courtesy.
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Left for the next save to replace.
        }
    }
}
