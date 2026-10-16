using System.Text.Json;

namespace Absolute.Core;

public sealed class PreferenceStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public AppPreferences Load()
    {
        try
        {
            var preferences = File.Exists(path)
                ? JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(path)) ?? new()
                : new AppPreferences();
            preferences.Benchmarks ??= [];
            return preferences;
        }
        catch (JsonException) { return new(); }
        catch (IOException) { return new(); }
        catch (UnauthorizedAccessException) { return new(); }
    }

    public void Save(AppPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, Options));
        File.Move(temporary, path, true);
    }
}
