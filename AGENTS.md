# Repository Guidelines

## Project Structure & Module Organization
`AlbionDataAvalonia/` contains the Avalonia UI, state management, and services; view logic lives under `ViewModels/` and XAML layouts under `Views/` and `Themes/`. `AlbionDataAvalonia.Desktop/` provides the desktop entry point (`Program.cs`) plus packaging assets; the `AlbionDataAvalonia.Desktop.*` directories hold platform-specific installers. Shared packet parsing and protocol models sit in `Albion.Network/`, `PhotonPackageParser/`, and `Protocol16/`. Default configuration, localization files, and migrations are grouped in their respective folders under the main project.

## Build, Test, and Development Commands
- `dotnet restore AlbionDataAvalonia.sln` – bootstrap dependencies.
- `dotnet build -c Debug AlbionDataAvalonia.sln` – compile all projects for local validation.
- `dotnet run --project AlbionDataAvalonia.Desktop` – launch the client with the desktop host.
- `dotnet publish AlbionDataAvalonia.Desktop -c Release -r win-x64` (or `linux-x64`, `osx-x64`) – create distributable binaries matching the installer targets.

## Coding Style & Naming Conventions
Use the repository defaults: C# with nullable reference types enabled (`Directory.Build.props`) and implicit usings. Prefer 4-space indentation, PascalCase for types and public members, camelCase for locals and fields, and suffix view models with `ViewModel`. Keep XAML element names meaningful and mirror corresponding view models. Run `dotnet format` before submitting to enforce spacing, imports, and analyzer hints.

## Testing Guidelines
No automated test project exists yet; create new test assemblies as needed using `dotnet new xunit -n AlbionDataAvalonia.Tests` and wire them into the solution. Name test classes after the unit under test with a `Tests` suffix, and methods using `MethodName_Scenario_ExpectedOutcome`. For UI or packet changes, attach manual verification notes—e.g., packet capture samples or UI screenshots—to document coverage.

## Commit & Pull Request Guidelines
Keep commits focused and write imperative, sentence-case messages, mirroring existing history (e.g., "Update LatestVersion.json"). For pull requests, include a succinct summary, affected OS targets, manual testing notes, and linked issues. Add screenshots when altering UI, and call out migrations or config changes that require user action.

## Configuration & Security Notes
Do not commit personal credentials or `AppData/` dumps; sample defaults live in `DefaultAppSettings.json` and `DefaultUserSettings.json`. When adjusting logging or network capture behavior (`Logging/`, `Network/`), double-check that verbose output stays behind user-controlled settings. Document any new environment variables or installer flags in the README and this guide.
