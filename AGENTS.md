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

## Release Branches & Versioning
- Never publish, create, edit, or delete a GitHub release or release tag unless the user explicitly instructs you to publish that specific release. Preparing release-related code, versions, or workflow changes does not authorize running the release workflow.
- Publish stable releases only from `master` and beta prereleases only from `beta`; the release workflow derives the channel from the selected branch.
- Keep `AlbionDataAvalonia/AlbionDataAvalonia.csproj` and `AlbionDataAvalonia.Desktop/pkg/inno.iss` on the same version before publishing.
- Stable and beta versions advance independently. A stable hotfix may remain below the active beta version (for example, stable `0.35.3.0` while beta is `0.36.2.0`). Do not assign old stable code a version above the active beta merely to make versions globally monotonic; beta clients could treat that older code as an upgrade, which is unsafe when database migrations differ.
- Publish a stable version equal to or greater than the active beta only after the beta code has been merged into `master` and is intentionally being promoted. When stable catches up to or exceeds beta, the workflow removes the beta entry from `LatestVersion.json`; beta users remain opted in, receive no downgrade, and become eligible again when the next beta is published.
- Never publish a numerically lower version within either channel, and do not bypass the updater or installer downgrade protections.

## Configuration & Security Notes
Do not commit personal credentials or `AppData/` dumps; sample defaults live in `DefaultAppSettings.json` and `DefaultUserSettings.json`. When adjusting logging or network capture behavior (`Logging/`, `Network/`), double-check that verbose output stays behind user-controlled settings. Document any new environment variables or installer flags in the README and this guide.
