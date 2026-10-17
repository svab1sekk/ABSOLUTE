namespace Absolute.Core;

public sealed record SettingAddress(string Key, string Name);
public sealed record SettingValue(string Kind, string Data);
public sealed record SettingEdit(SettingAddress Address, SettingValue Value);
public sealed record SettingBackup(SettingAddress Address, SettingValue? Original, SettingValue Applied);

public interface ISettingsBackend
{
    SettingValue? Read(SettingAddress address);
    void Write(SettingAddress address, SettingValue? value);
}

/// <summary>Only restores values still owned by this transaction. Never overwrites later external changes.</summary>
public static class SettingsTransaction
{
    public static IReadOnlyList<SettingBackup> Capture(ISettingsBackend backend, IEnumerable<SettingEdit> edits) =>
        edits.Select(e => new SettingBackup(e.Address, backend.Read(e.Address), e.Value)).ToArray();

    public static void Apply(ISettingsBackend backend, IReadOnlyList<SettingBackup> journal)
    {
        try { foreach (var entry in journal) backend.Write(entry.Address, entry.Applied); }
        catch
        {
            foreach (var entry in journal.Reverse())
                if (backend.Read(entry.Address) == entry.Applied) backend.Write(entry.Address, entry.Original);
            throw;
        }
    }

    public static void Restore(ISettingsBackend backend, IReadOnlyList<SettingBackup> journal)
    {
        var conflicts = journal.Where(e => backend.Read(e.Address) is var current && current != e.Applied && current != e.Original).ToArray();
        if (conflicts.Length > 0) throw new InvalidOperationException("A setting was changed outside ABSOLUTE. No settings were restored: " + string.Join(", ", conflicts.Select(e => e.Address.Name)));
        foreach (var entry in journal.Reverse())
            if (backend.Read(entry.Address) == entry.Applied) backend.Write(entry.Address, entry.Original);
    }
}
