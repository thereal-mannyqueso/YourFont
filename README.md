# FontBook for Windows

A native Windows font manager inspired by Apple's Font Book, combining a minimal macOS-style
layout with Windows 11 materials (Mica, rounded layers, Segoe Fluent icons).

Built with **C# / .NET 10 / WPF**, MVVM, dependency injection and SQLite.

> **Status: Milestone 1 (foundation).** The interface, navigation, previews, themes,
> favorites, collections and database work. The font list currently comes from a built-in
> **sample data set** (marked with a "Sample data" badge). Real font discovery, importing and
> installation are the next milestones. See [Roadmap](#roadmap).

---

## Requirements

| Tool | Version | Notes |
|---|---|---|
| Windows | 10 or 11 | Mica backdrop needs Windows 11 22H2+. Older versions get a solid background. |
| .NET SDK | 10.0 | `winget install Microsoft.DotNet.SDK.10` |
| Git | any recent | `winget install Git.Git` |
| IDE (optional) | Visual Studio 2026 / VS Code / Rider | Open `FontBook.slnx` |

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

## What works in Milestone 1

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
    Search/                    FontSearch: search matching rules
  FontBook.Infrastructure/     Implementations of the Core contracts
    Database/                  SQLite connection, schema migrations, repositories
    Fonts/Sample/              TEMPORARY sample font data (Milestone 1 only)
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
2. **Milestone 2 — Real fonts:** discover installed fonts (system and per-user) from the Windows font
   registry and files. Import TTF/OTF/TTC into the Font Vault (copies, originals untouched) and preview them without installing.
3. **Milestone 3 — Install/uninstall:** per-user installation through documented Windows mechanisms,
   safe uninstall with confirmation, install from the Vault
4. **Later:** font details (designer, version, license), glyph grid, variable-font axes, drag-and-drop import,
   themed dialogs, window size/position memory, app icon, installer/packaging

## Documentation

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): how the app is structured and why
- [CLAUDE.md](CLAUDE.md): working conventions for AI-assisted development on this repository
