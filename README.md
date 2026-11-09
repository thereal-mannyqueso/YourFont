# FontBook for Windows

A native Windows font manager inspired by Apple's Font Book, combining a minimal macOS-style
layout with Windows 11 materials (Mica, rounded layers, Segoe Fluent icons).

Built with **C# / .NET 10 / WPF**, MVVM, dependency injection and SQLite.

> **Status: Milestone 2 complete.** FontBook shows the fonts installed on your PC (system fonts and
> fonts you installed for your account) and lets you import font files into its **Font Vault** to
> preview them without installing. Installed fonts are only *read*. Installing and uninstalling fonts
> is next. See [Roadmap](#roadmap).

---

## Requirements

| Tool | Version | Notes |
|---|---|---|
| Windows | 10 or 11 | Mica backdrop needs Windows 11 22H2+. Older versions get a solid background. |
| .NET SDK | 10.0 | `winget install Microsoft.DotNet.SDK.10` |
| Git | any recent | `winget install Git.Git` |
| IDE (optional) | Visual Studio 2026 / VS Code / Rider | Open `FontBook.slnx` |

> **Smart App Control:** Windows 11's Smart App Control blocks unsigned programs you build yourself
> ("An Application Control policy has blocked this file"). On a development PC it has to be turned off
> (Windows Security → App & browser control), or builds must be code-signed.

## Build, test, run

```bash
dotnet build FontBook.slnx
```

```bash
dotnet test FontBook.slnx
```

```bash
dotnet run --project src/FontBook.App
```

The app keeps its data in `%LOCALAPPDATA%\FontBook\` (`fontbook.db`, and later the `Vault` folder).
Deleting that folder resets FontBook to a fresh state. It never touches your installed fonts.

## What works

- **Real font discovery (read-only):** reads Windows' installed-font list (`HKLM` for system fonts, `HKCU` for
  fonts installed just for you), opens each TTF/OTF/TTC file read-only, and groups faces into families.
  Each style is drawn from its own file. Legacy `.fon` bitmap fonts are skipped (modern renderers can't draw them).
- **Font Vault import:** import TTF/OTF/TTC/OTC files with the Import button (sidebar or empty state), **Ctrl+O**, or by
  dragging files or folders onto the window. FontBook copies them into `%LOCALAPPDATA%\FontBook\Vault`
  (originals untouched), detects duplicates by content, rejects damaged files, and previews them without installing.
  Imported fonts appear under **Imported Fonts** and **Recently Added** (last 30 days).
- **Remove from Font Vault** (preview header or right-click) deletes only FontBook's copy, after confirmation.
- **Style categories:** every font is sorted into Serif, Sans Serif, Monospace, Script & Handwritten,
  Display & Fun, Symbol or Other. FontBook detects this from data inside the font file (IBM font class, PANOSE,
  fixed-pitch flag), falling back to name clues. **Filter chips** above the list combine with any section
  (e.g. User Fonts + Script). Click the category next to a font's name in the preview to change it; your choice is saved.
- **Install / uninstall for your account** (Milestone 3): **Install** on Vault fonts copies the font into your
  per-user fonts folder, registers it under `HKCU` and notifies apps. **Uninstall…** on User Fonts asks first and can
  keep a copy in the Vault. System fonts can never be uninstalled from FontBook. *Status: unit-tested with fakes;
  a real install/uninstall on Windows hasn't been verified yet.*
- Main window with a custom Windows 11 title bar, Mica backdrop and native caption buttons (Snap Layouts work)
- Resizable sidebar and font list (widths are remembered)
- Sidebar navigation: **Library** (All, System, User, Favorites) and **Font Vault** (Imported, Custom Collections, Recently Added) with counts
- Font list showing each family name in its own typeface
- Preview pane: large specimen, custom sample text, size slider (8–200 pt), and every style rendered below. Click a style to preview it large.
- Search across family and style names (`Ctrl+F`, `Esc` clears). Multiple words narrow results, e.g. `georgia bold`.
- Favorites (star in the list or the preview header)
- Custom collections: create (`+`), rename inline, delete (with confirmation), add/remove fonts from the preview's **Collections** menu
- Light, Dark and Auto (follows Windows) themes, switching live
- SQLite persistence for settings, favorites and collections, with schema versioning
- Helpful empty states for every section

## Project layout

```
FontBook.slnx                  Solution (open this)
Directory.Build.props          Settings shared by all projects
src/
  FontBook.Core/               Models, interfaces, business rules. No UI, no database.
    Models/                    FontFamilyInfo, FontFaceInfo, FontSource, FontCollection, …
    Services/                  IFontDiscoveryService, IFontInstallationService (contracts)
    Data/                      Repository contracts + setting keys
    Library/                   FontLibraryService: favorites, collection membership, filtering
    Collections/               CollectionService: naming and validation rules
    Classification/            FontClassifier: automatic style categories
    Search/                    FontSearch: search matching rules
  FontBook.Infrastructure/     Implementations of the Core contracts
    Database/                  SQLite connection, schema migrations, repositories
    Fonts/Files/               Read-only OpenType/TrueType name reader
    Fonts/Discovery/           Installed-font discovery (registry list + files)
    Vault/                     Font Vault: import (copy + hash), remove, clean-up
  FontBook.App/                The WPF desktop application
    Views/                     Windows and user controls (XAML)
    ViewModels/                MVVM view models
    Controls/                  Reusable controls (FontPreview)
    Themes/                    Light/Dark palettes, control styles
    Services/                  Theme, window effects, dialogs, rendering, deferred settings
    Converters/, Behaviors/    Small XAML helpers
    Composition/               Dependency injection setup
tests/
  FontBook.Tests/              xUnit tests for Core and Infrastructure
docs/
  ARCHITECTURE.md              Design decisions and their trade-offs
```

## Roadmap

1. ✅ **Milestone 1 — Foundation:** shell, navigation, previews, themes, collections, database
2. **Milestone 2 — Real fonts:**
   - ✅ Discover installed fonts (system and per-user) from the Windows font registry and files
   - ✅ Import TTF/OTF/TTC into the Font Vault (copies, originals untouched) and preview them without installing
3. **Milestone 3 — Install/uninstall:** per-user installation through documented Windows mechanisms,
   safe uninstall with confirmation, install from the Vault
4. **Later:** font details (designer, version, license), glyph grid, variable-font axes, drag-and-drop import,
   themed dialogs, window size/position memory, app icon, installer/packaging

## Documentation

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): how the app is structured and why
- [CLAUDE.md](CLAUDE.md): working conventions for AI-assisted development on this repository
