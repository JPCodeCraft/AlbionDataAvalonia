# Repository Guidelines

## Shared Core
- Shared Photon decoding, packet models, passive upload handlers, farming, reference data, and PoW live in `shared/AFMDataClientCore`, a pinned Git submodule of `JPCodeCraft/AFMDataClientCore`.
- Run `git submodule update --init --recursive` after cloning or changing branches. Make shared fixes in that repository, commit/push them there, then update this repository's submodule pointer deliberately.
- Keep OS capture, authentication storage/UI, settings, EF persistence, and desktop trackers in this application. Register shared features explicitly in `DesktopClientCore`; add local packet subscriptions after shared handlers in `NetworkListenerService`.
- The desktop Photon parser is the authoritative implementation for both clients. Do not add another parser or duplicate shared upload transports.

## Build, Test, and Development Commands
- Only add, remove, or apply EF Core migrations when explicitly instructed. Don't modify migration files manually: only use EF commands.
- `dotnet restore AlbionDataAvalonia.sln` – bootstrap dependencies.
- `dotnet build -c Debug AlbionDataAvalonia.sln` – compile all projects for local validation.

## Coding Style & Naming Conventions
Use the repository defaults: C# with nullable reference types enabled (`Directory.Build.props`) and implicit usings. Prefer 4-space indentation, PascalCase for types and public members, camelCase for locals and fields, and suffix view models with `ViewModel`. Keep XAML element names meaningful and mirror corresponding view models. Run `dotnet format` before submitting to enforce spacing, imports, and analyzer hints.

## UI Conventions
- Colors, radii, padding and font sizes come from `Themes/Tokens.axaml` through `{DynamicResource ...}`. Don't type hex or rgba colors, or numeric font sizes, in XAML; add a token instead. Code that draws (the timeline and map canvases) reads tokens through `UiPalette` and `ActivityPalette`, and uses `ChartDrawing` for text, tooltips and badges. `scripts/check-ui-tokens.sh` enforces this in CI.
- View models expose states, never brushes or colors. XAML maps a state to tokens with classes (`Classes.Done="{Binding IsDone}"`) or, for a kind with many values, a converter that reads `ActivityPalette`.
- Text uses the classes in `Themes/Typography.axaml`: `UiCaption`, `UiLabel`, `UiFine`, `UiMuted`, `UiRowTitle`, `UiSectionTitle`, `UiTitle`, `UiHeading`, `UiValue`, `UiValueLarge`, `UiAccent`, `UiWarning`, `UiError`, `UiMono`. Use the `UiTextSecondaryBrush` and `UiTextTertiaryBrush` tokens for muted text, not `Opacity`. Use `SemiBold` for emphasis and `Bold` only for large values.
- A plain `Button` is the secondary button. Give a button at most one role class from `Themes/Buttons.axaml`: `UiPrimary` (one per view or dialog), `UiDanger` (deletes and other actions that can't be undone), `UiGhost` or `UiLink`. Add `UiAction` for the standard action size. Disabled buttons keep their role. Actions that open a dialog end with "…".
- Cards, banners, chips and badges use the classes in `Themes/Surfaces.axaml` (`UiCard` with `Subtle`, `Bordered`, `Outlined` or `Sunken`, plus `UiBanner`, `UiChip`, `UiBadge`, `UiCode` and `UiOverlay`). A local style block needs a comment saying why it can't be shared.
- Reuse the controls in `Views/Controls` (looks in `Themes/ControlThemes.axaml`) before building a layout by hand: `SettingsGroup` and `SettingsRow` for settings, `StatTile` for a label over a value, `EmptyState` for an empty list or table, `InfoBanner` for a notice, `FilterField` for a labelled filter, `ItemCell` for an item's icon and name, `SegmentedSelector` for a single choice, `ToggleButton` with `Theme="{StaticResource UiToggleChip}"` for choices where several can be on, and `ToggleButton.UiLegend` for a chart legend entry that shows or hides its series.
- Dialogs derive from `AppDialog<TResult>` in `Views/Dialogs` and are opened through `IDialogService` (`ConfirmAsync`, `ConfirmDeleteAsync`, `AlertAsync`, `ShowAsync`). View models reach the clipboard and file pickers through `IClipboardService` and `IFileDialogService`. Button handlers that open dialogs, copy text or pick files belong in view-model commands, not code-behind; code-behind is for view-only work such as scrolling, zooming or passing a grid's selection to the view model.
- A secondary window that has one instance at a time (the streamer, the map) uses `SingleWindowHost`, which remembers whether it was open and where it was placed.
- View models never create views or windows. `MainViewModel.CurrentPage` holds the page's view model and `MainView` maps it to a view with data templates; windows are created by `App`, `SingleWindowHost` or a dialog.
- Keep a view model to one screen or panel, and move self-contained parts out as they grow: the Main tab checklist is `SetupStatusViewModel`, private sharing is `PrivateSharingViewModel` and Portfolio uploads go through `PortfolioImportCoordinator`. The Activity page (`ActivityViewModel`) holds recording state and the refresh loop, and each tab has its own view model (`ActivityCurrentViewModel`, `ActivityHistoryViewModel`, `ActivityItemsViewModel`); the tabs share `ActivityFilterState` (scope and the Show kinds) and `ActivityLootExport`. Activity code reads its tabs through `ActivityTab` and `ActivityDetailTab`, never index numbers.
- When many displayed values change together, build them as one record and set it once (as `ActivityDetailHeader` does for the History header) instead of raising a notification for each; an equal record raises nothing.
- Pages open with one toolbar band: tabs or filters on the left, actions on the right. There are no page titles; the sidebar names the page.
- Pages and windows set `Classes="AppPage"` to get the shared input, expander, grid and switch skins from `Themes/Controls.axaml`. Windows also set the app icon.
- Format numbers, sizes, durations and times with `UiFormat` (`Count`, `Whole`, `Compact`, `Bytes`, `Duration`, `Clock`, `Ago`, `LocalTime`…), so plurals and clocks read the same everywhere. Shared words live in `UiText`: "Any" and "All" for filters, and `Unknown`, `Unavailable`, `NotRecorded` and `None` ("—") for missing values.
- Choices use `Option<T>`; lists more than one screen offers live in `UiOptions` (trade and log counts, servers).
- Service events arrive on other threads: start a handler with `if (UiDispatch.Repost(() => Handler(args))) return;`. Use `UiDebouncer` to wait for input to settle and `LatestLoad` so only the newest of overlapping loads applies its result.
- Write copy in sentence case. Cross-references read "Settings › Tab". Reload actions are called "Refresh".
- Icon-only buttons set `AutomationProperties.Name`, and every `DataTemplate` declares `x:DataType`.
- Debug builds open a UI kit window (`Views/UiKitWindow.axaml`) with every token, text class, button role, surface and shared control when `AFM_UI_KIT=1` is set. Add new shared pieces to it, and include a screenshot of it in pull requests that change tokens.

## Testing Guidelines
PoW tests live in `PowBench.Tests`; run them with `dotnet test PowBench.Tests`. Keep tests focused on PoW unless explicitly instructed otherwise. See `PowBench/README.md` for algorithm speed comparisons.

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
