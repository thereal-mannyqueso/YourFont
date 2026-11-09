namespace FontBook.Core.Data;

/// <summary>Simple persistent key/value store for user preferences.</summary>
public interface ISettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
}

/// <summary>Names of the settings FontBook stores. Keep them here so they are never mistyped.</summary>
public static class SettingKeys
{
    public const string Theme = "appearance.theme";
    public const string SidebarWidth = "layout.sidebarWidth";
    public const string FontListWidth = "layout.fontListWidth";
    public const string PreviewSize = "preview.size";
    public const string SpecimenText = "preview.specimenText";
}
