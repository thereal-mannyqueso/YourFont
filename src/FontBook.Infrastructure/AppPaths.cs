namespace FontBook.Infrastructure;

/// <summary>
/// Where FontBook keeps its own files. Everything lives under
/// %LOCALAPPDATA%\FontBook so it is per-user and never mixed with Windows' font folders.
/// </summary>
public sealed class AppPaths
{
    public AppPaths()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FontBook"))
    {
    }

    /// <summary>Use a custom root folder (used by tests).</summary>
    public AppPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = rootDirectory;
    }

    public string RootDirectory { get; }

    public string DatabasePath => Path.Combine(RootDirectory, "fontbook.db");

    /// <summary>Folder for imported font copies (used from Milestone 2 onward).</summary>
    public string VaultDirectory => Path.Combine(RootDirectory, "Vault");

    public void EnsureCreated() => Directory.CreateDirectory(RootDirectory);
}
