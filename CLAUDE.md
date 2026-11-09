# CLAUDE.md — working on FontBook for Windows

Guidance for AI assistants (and humans) contributing to this repository.
Read `docs/ARCHITECTURE.md` for the design rationale.

## Product context
- Native Windows font manager inspired by Apple's Font Book. Apple-minimal layout + Windows 11 materials.
- The product owner is learning software development. **Explain important decisions in plain language,
  present trade-offs with a recommendation, and never claim a feature works until it has been tested.**
- Work in small, verifiable milestones. Build and test after each significant change.

## Commands (PowerShell; .NET 10 SDK)
```bash
dotnet build FontBook.slnx            # must finish with 0 warnings, 0 errors
dotnet test FontBook.slnx             # xUnit tests in tests/FontBook.Tests
dotnet run --project src/FontBook.App # launch the app
```
- Close any running `FontBook.exe` before building, or the build fails because the exe is locked.
- If `dotnet`/`git` are "not recognized" in an old shell, refresh PATH:
  `$env:Path = [Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [Environment]::GetEnvironmentVariable("Path","User")`
- App data: `%LOCALAPPDATA%\FontBook\fontbook.db`. Delete the folder to reset.

## Architecture rules
- **Core** (`src/FontBook.Core`): models, interfaces, business rules. No WPF, SQLite or Windows APIs.
- **Infrastructure** (`src/FontBook.Infrastructure`): SQLite and Windows implementations of Core contracts.
- **App** (`src/FontBook.App`): WPF only. Views bind to view models. **No business logic in code-behind.**
  Code-behind is allowed only for window plumbing (backdrop, focus, maximize margins).
- Register new services in `Composition/AppServices.cs` (App/Core) or
  `InfrastructureServiceCollectionExtensions.cs` (Infrastructure).
- Font installation/removal must use documented Windows mechanisms (see `IFontInstallationService`).
  Never modify files in `C:\Windows\Fonts`. Never modify a user's original font files.
- Imported (Vault) fonts must stay separate from installed fonts. `FontKey` includes the source for this reason.
- Sample data lives only in `Infrastructure/Fonts/Sample/` and must stay clearly flagged (`IsSample`, UI badge).

## Database
- Schema changes: **append** a migration to `DatabaseInitializer.Migrations`. Never edit an existing one.
- Repositories use parameterized SQL (`$name` parameters). Never build SQL from user input.
- Dates are stored as ISO-8601 UTC text (`DbTime`).

## UI conventions
- Colors: only via `DynamicResource` keys defined in **both** `Themes/Light.xaml` and `Themes/Dark.xaml`.
- Styles live in `Themes/Controls.xaml`; custom control templates in `Themes/Generic.xaml`.
- Animations: short (120–240 ms), ease-out, and fade the `Opacity` of a layer (palette brushes are frozen and can't be animated).
- Selection must drive behavior: bind `IsChecked`/`IsSelected` **TwoWay** and react in the view model,
  rather than relying on click `Command`s. Otherwise keyboard and UI Automation users can't navigate.
- Give interactive elements an `AutomationProperties.Name`.
- Fonts: UI uses `{StaticResource UiFont}` / `DisplayFont`. Icons use `IconFont` (Segoe Fluent Icons).

## Pitfalls learned the hard way
- **Icon glyphs in C#:** never put Private Use Area characters or `\uE...` escapes in C# source.
  Tools can silently corrupt them. Use `ViewModels/Glyphs.cs` (`char.ConvertFromUtf32(0xE8D2)`).
  In XAML use `&#xE8D2;` entities.
- **Encoding:** Windows PowerShell 5.1 `Get-Content`/`Set-Content` default to ANSI and corrupt UTF-8.
  Edit files with the editor tools, or always pass `-Encoding utf8`.
- WPF projects don't import `System.IO` implicitly. `src/FontBook.App/GlobalUsings.cs` adds it.
- `SupportedOSPlatformVersion` can't exceed 7.0 with `net10.0-windows` unless you target a Windows SDK TFM. Use runtime checks (`WindowEffects.IsMicaSupported`).

## Verifying UI changes
Build, launch, and inspect. Driving the app through Windows UI Automation (the
`System.Windows.Automation` client API) works well for automated checks: select sidebar items by
their `AutomationProperties.Name`, set text, then screenshot the window. Check both Light and Dark themes.
