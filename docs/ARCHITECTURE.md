# FontBook architecture

This document explains how FontBook is put together and why. It is written for the product
owner as much as for developers: each decision lists the alternatives and the trade-off.

## The big picture

```
┌────────────────────────────── FontBook.App (WPF) ──────────────────────────────┐
│ Views (XAML) ──binds to──▶ ViewModels ──calls──▶ App services (theme, dialogs,  │
│                                  │                 rendering, settings writer)  │
└──────────────────────────────────┼─────────────────────────────────────────────┘
                                   │ uses interfaces only
┌──────────────────────────── FontBook.Core ─────────────────────────────────────┐
│ Models · FontLibraryService · CollectionService · FontSearch                    │
│ Contracts: IFontDiscoveryService, IFontInstallationService, repositories        │
└──────────────────────────────────▲─────────────────────────────────────────────┘
                                   │ implements
┌──────────────────────────── FontBook.Infrastructure ───────────────────────────┐
│ SQLite (connection, migrations, repositories) · Font discovery (sample for now) │
└────────────────────────────────────────────────────────────────────────────────┘
```

**Rule of thumb:** Core knows nothing about WPF, SQLite or Windows. Infrastructure knows about
SQLite and Windows but not WPF. Only the App project knows about WPF. Dependencies point inward
toward Core, so business rules can be tested without starting the UI.

## Key decisions

### 1. Three projects instead of one
- **Chosen:** Core / Infrastructure / App (+ Tests).
- **Alternative:** a single WPF project with folders. Simpler at first.
- **Why:** the compiler enforces the boundaries. A view model *cannot* write SQL, and Core *cannot*
  reference WPF, because those projects simply don't reference each other. That keeps business
  logic out of code-behind as the app grows.

### 2. MVVM with CommunityToolkit.Mvvm
- **Chosen:** Microsoft's CommunityToolkit.Mvvm. Source generators turn `[ObservableProperty]`
  and `[RelayCommand]` into the boilerplate WPF needs.
- **Alternatives:** hand-written `INotifyPropertyChanged` (lots of repetitive code), Prism or
  ReactiveUI (powerful but heavier, and more concepts to learn).
- **Why:** smallest dependency that removes the boilerplate; maintained by Microsoft.

### 3. Dependency injection
- **Chosen:** `Microsoft.Extensions.DependencyInjection`, configured in `Composition/AppServices.cs`
  (and `AddFontBookInfrastructure` for the data layer).
- **Why:** classes receive what they need through their constructor, so they can be tested with
  fakes, and swapping an implementation is a one-line change. Milestone 2 will replace
  `SampleFontDiscoveryService` with the real one exactly this way.

### 4. Custom theme instead of a UI library
- **Chosen:** our own `Themes/Light.xaml`, `Themes/Dark.xaml` (colors) and `Themes/Controls.xaml`
  (styles), plus Mica through the documented `DwmSetWindowAttribute` API.
- **Alternatives:**
  - *WPF-UI (lepoco)*: a good Fluent library, but it imposes its own look and controls. We want an
    Apple/Windows hybrid, not stock Fluent.
  - *.NET's built-in `ThemeMode="System"` Fluent theme*: still marked experimental (WPF0001)
    and restyles every control in ways that fight custom styles.
- **Why:** full control over the hybrid design, no extra dependency, and no experimental APIs. The cost
  is that we style each control ourselves (done in `Controls.xaml`).
- **How theme switching works:** every style uses `DynamicResource` brushes. `ThemeService` swaps
  the palette dictionary at runtime, and in Auto mode it listens for Windows' light/dark change.

### 5. Custom title bar with native caption buttons
- `WindowChrome` with `GlassFrameThickness=-1` extends our content into the title bar while
  keeping the real Windows minimize/maximize/close buttons (with Snap Layouts on hover).
- If Mica isn't available (before Windows 11 22H2), `MainWindow` removes the chrome and falls back to
  the normal title bar with a solid background.

### 6. Font rendering and previewing uninstalled fonts
- **Chosen for now:** WPF's own text engine through the `IFontPreviewProvider` abstraction
  (`Services/Rendering`). WPF renders OpenType/TrueType/TTC with high-quality anti-aliasing, and
  can load a font **directly from a file without installing it**:
  `new FontFamily(new Uri("file:///…/Vault/"), "./#Family Name")`. `GlyphTypeface` exposes name-table
  metadata (designer, version, copyright, license) with no extra library.
- **Alternative:** direct DirectWrite via Vortice.DirectWrite. Needed for advanced features such as
  variable-font axis sliders and color-font layers, but it means interop and our own rendering surface.
- **Recommendation:** start with WPF (zero dependencies, covers previews for TTF/OTF/TTC). Because
  views only see `IFontPreviewProvider` and `FontPreview`, a DirectWrite renderer can be added later
  without touching the rest of the app.
- Vault fonts will be previewed from **FontBook's own copy** in `%LOCALAPPDATA%\FontBook\Vault`,
  never from the user's original file, so originals are never locked or modified.

### 7. SQLite with plain SQL (no ORM)
- **Chosen:** `Microsoft.Data.Sqlite` with small hand-written repositories.
- **Alternative:** Entity Framework Core. Convenient, but a large dependency for four small tables.
- **Schema changes:** `DatabaseInitializer` keeps an ordered list of migrations and SQLite's
  `user_version` number. Each start-up applies any missing migrations inside a transaction.
  **Never edit an existing migration. Append a new one.**
- WAL journaling protects the database if the app crashes mid-write.

| Table | Purpose |
|---|---|
| `settings` | key/value preferences (theme, pane widths, preview size, sample text) |
| `favorites` | font keys the user starred |
| `collections` | user collections (names are unique, ignoring case) |
| `collection_fonts` | which fonts are in which collection (deleted automatically with the collection) |
| `vault_files` (v2) | one row per imported file: content hash, stored name, original name/path, size, import time |

### 8. Font keys
Favorites and collections store a **font key**, such as `system:segoe ui` or `vault:inter`.
The source is part of the key, so an imported Vault copy and an installed font with the same
name are never confused. Removing a Vault copy can never affect a Windows font.

### 9. In-memory library state
`FontLibraryService` loads fonts, favorites and memberships once, then answers queries from
memory, so search is instant while typing. Every change is written to SQLite first and then applied
in memory, so the screen never shows something that wasn't saved.

### 10. Selection drives navigation (accessibility)
Sidebar entries and style rows are radio buttons whose `IsChecked` is bound two-way to
`IsSelected` in the view model. Navigation reacts to that property, not to a click command, so
mouse, keyboard and assistive technology (screen readers via UI Automation) all behave the same.
This was found and fixed during Milestone 1 testing.

## Font discovery (Milestone 2)

Milestone 1 used a temporary sample list. It was replaced (and deleted) once real discovery worked.

1. **Which fonts are installed?** `WindowsRegistryFontFileSource` reads the same list Windows
   Settings uses, with registry keys opened read-only:
   - `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts` → **System** (for all users, files in `C:\Windows\Fonts`)
   - `HKCU\...\Fonts` → **User** (installed only for you, files usually in `%LOCALAPPDATA%\Microsoft\Windows\Fonts`)
2. **What's in each file?** `FontFileReader` is a small, dependency-free OpenType reader. It seeks
   to the `name`, `OS/2` and `head` tables (a few KB, even for huge fonts) to get family, style,
   weight, width and italic. Files are opened with `FileAccess.Read` and a permissive share mode, so
   FontBook can never modify or lock them. A test verifies files are byte-for-byte unchanged.
3. **Grouping:** faces are grouped into families by *typographic* family name (name ID 16), so
   "Segoe UI Semibold" appears as the Semibold style of "Segoe UI", like Font Book does.
   Discovery runs on a background thread. Unreadable or unsupported files are skipped and logged.

**Rendering each face.** Every style is drawn from its own file:
`new FontFamily(fileUri, "#<name>")`. The name must be the one WPF matches on: the WWS family
name (ID 21), else typographic (ID 16), else legacy (ID 1). Passing any other name doesn't fail. WPF
**silently falls back to Segoe UI**, which is why `RenderName_MatchesWpf_ForEveryInstalledFont`
checks every installed font against WPF's own `GlyphTypeface.FamilyNames`.

**Known limits:** `.fon` bitmap fonts are skipped. Variable fonts (e.g. Bahnschrift, Segoe UI Variable)
show only their default style for now; named instances need the DirectWrite renderer described above.

## Font Vault (Milestone 2)

`IFontVault` (Core) / `FontVaultService` (Infrastructure). The Vault is FontBook's own folder,
`%LOCALAPPDATA%\FontBook\Vault`, completely separate from Windows' font folders.

- **Import:** each file is opened read-only, hashed (SHA-256), and copied to `Vault\<hash>.<ext>`.
  The *copy* is then parsed to confirm it's a real font before the database row is written. Any failure
  rolls back the copy. Content-hash names make duplicates (even renamed ones) easy to detect and store once.
  Folders are searched recursively. Problems are reported per file instead of aborting the import.
- **Preview without installing:** Vault families are built from the copies with the same reader and
  grouping as installed fonts, and drawn from the copies with the same file-based rendering. Windows never sees them.
- **Remove:** allowed only for `FontSource.Imported` families. Every file whose faces belong to the family is
  removed from the database and deleted, and each delete is checked to be inside the Vault folder. If Windows still
  has a previewed file open, the row is removed anyway and `CleanUpAsync` (run at start-up) deletes the leftover file.
- **Library:** `FontLibraryService` merges installed and Vault families; `RefreshVaultAsync` reloads just the Vault after changes.
- **Keys:** Vault fonts use `vault:<family>` keys, so favorites and collections never mix them up with installed fonts.

## Planned: installation (Milestone 3)
`IFontInstallationService` documents the intended, supported mechanism: per-user installation
(Windows 10 1809+, no admin rights). Copy into `%LOCALAPPDATA%\Microsoft\Windows\Fonts`, register under
`HKCU\Software\Microsoft\Windows NT\CurrentVersion\Fonts`, then call `AddFontResourceEx` and
broadcast `WM_FONTCHANGE`. Uninstall reverses this for per-user fonts only, after confirmation.
System fonts in `C:\Windows\Fonts` are never modified.
