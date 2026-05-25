# Repository Guidelines

## Build, Test, and Development Commands
- Only add, remove, or apply EF Core migrations when explicitly instructed. Don't modify migration files manually: only use EF commands.
- `dotnet restore AlbionDataAvalonia.sln` – bootstrap dependencies.
- `dotnet build -c Debug AlbionDataAvalonia.sln` – compile all projects for local validation.

## Coding Style & Naming Conventions
Use the repository defaults: C# with nullable reference types enabled (`Directory.Build.props`) and implicit usings. Prefer 4-space indentation, PascalCase for types and public members, camelCase for locals and fields, and suffix view models with `ViewModel`. Keep XAML element names meaningful and mirror corresponding view models. Run `dotnet format` before submitting to enforce spacing, imports, and analyzer hints.

## Testing Guidelines
We don't use tests. Do not add them.

## Commit & Pull Request Guidelines
Keep commits focused and write imperative, sentence-case messages, mirroring existing history (e.g., "Update LatestVersion.json"). For pull requests, include a succinct summary, affected OS targets, manual testing notes, and linked issues. Add screenshots when altering UI, and call out migrations or config changes that require user action.

## Configuration & Security Notes
Do not commit personal credentials or `AppData/` dumps; sample defaults live in `DefaultAppSettings.json` and `DefaultUserSettings.json`. When adjusting logging or network capture behavior (`Logging/`, `Network/`), double-check that verbose output stays behind user-controlled settings. Document any new environment variables or installer flags in the README and this guide.
