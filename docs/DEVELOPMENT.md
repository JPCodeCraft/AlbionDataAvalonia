# Development notes

Technical notes for contributors. For installing and using the client, see the [README](../README.md).

## Building with the shared core

The desktop and Android clients share [AFMDataClientCore](https://github.com/JPCodeCraft/AFMDataClientCore). This repository pins its library at `shared/AFMDataClientCore`; no sibling checkout or NuGet feed is required to build.

```bash
git clone --recurse-submodules https://github.com/JPCodeCraft/AlbionDataAvalonia.git
cd AlbionDataAvalonia
git submodule update --init --recursive
dotnet restore AlbionDataAvalonia.sln
dotnet build -c Debug AlbionDataAvalonia.sln
dotnet test PowBench.Tests
```

After switching branches or pulling a change to the submodule pointer, run `git submodule update --init --recursive` again. Shared changes are committed and pushed in AFMDataClientCore first; each client then updates its pinned commit independently. A standalone development checkout can live beside the clients at `Repos/AFMDataClientCore`, while builds always reference the pinned submodule.

`DesktopClientCore` explicitly enables market orders (including loadouts), market history, specs, EMV, islands, gold, and world events. It adapts desktop settings, account credentials, and upload statistics to the shared runtime. Activity, party, awakened items, market mail, and trade history remain desktop services and subscribe after shared packet processing. Capture drivers, database schemas, installers, and UI stay in the desktop repository.

The app embeds gzipped base copies of its reference data (items, locations, achievements, mobs and awakened-item data) from `AlbionDataAvalonia/ReferenceData`. They are used only when a file cannot be downloaded and has no validated disk cache yet, such as a first start without access to the CDN. Run `AlbionDataAvalonia/ReferenceData/update.sh` before a release to refresh them from the CDN.

Shared-library integration validation includes building the desktop solution and running the PoW correctness tests. Manual checks cover ordinary and loadout market routing, gold and bandit uploads during Private Mode, authenticated specs/EMV/world events, local tracker updates, account/server changes, and island collection, failed actions, offline retry, and restart recovery. Cross-platform capture and UI checks require the corresponding operating systems; builds alone do not validate live game traffic.

## How Activity records

Activity combines **Current**, **History**, **Items**, and **Cleanup**. Sessions contain encounters and activity between fights; session totals include every assigned event directly. Personal and Party filters use membership saved when each event occurred. Party includes your character. Items also supports All observed and source filters for loot, gathering, and fishing. **Total Silver** and **Total Silver / Hour** combine cash pickups with estimated loot, gathering, and fishing value for the selected period and scope; their tooltip explains estimates and missing values. **Loot value** includes loot only; gathered items, their EMV, and Gathering Fame share the gathering panel.

Current includes a rolling **5, 10, or 15 minute** timeline with damage/healing areas, grouped outcome and reward markers, and encounter combat spans. Nearby rewards are grouped more strongly for longer windows. **Dealt / Received** changes the health series. Hover holds the display while recording continues; moving the mouse out resumes it automatically. Markers show compact tooltips. Hovering an encounter span shows its location, combat time and saved totals for the selected scope (damage, healing, fame, silver, loot, kills, deaths), and clicking it opens the encounter in History. History charts use the same display over the whole session or selected encounter, with at most 600 rate bins. The graph includes idle zeros; headline DPS/HPS uses combat time. Capture gaps are shown separately from downtime. **Gathering** chart mode shows harvested and fished items per second on a separate scale. **Show** filters apply to charts and events, leaving whole-period cards unchanged; selected categories match any. History **Contains all** tags match every selected tag. Loot and silver in PvE/PvP views remain earnings observed during matching encounters; fame uses its independently matched source.

Relevant damage or attributable healing opens an encounter and refreshes its seven-second inactivity deadline. Loot, silver, fame and outcome signals never extend combat. Encounter DPS/HPS uses the first-to-last combat span with a one-second minimum. Encounter earnings mean **observed during the encounter**, without claiming the fight produced them.

Successful instance entry becomes a session only when relevant activity is observed. Supported dungeon floors can continue one run. Known-instance reentry within two minutes can resume a run when no intervening relevant activity or local lethal death prevents it. Roaming survives ordinary zone changes and ends on hub return, instance entry, Finish, character/server change or 30 minutes without relevant activity. Session DPS/HPS uses the combined combat spans of encounters with activity in the selected scope, excluding time between fights and the closing delay. Earnings per hour use observed session time, including travel and downtime but excluding explicit capture gaps and dungeon-exit intervals.

Mob counts require prior personal/party damage in the same lifetime. Explicit zero health confirms terminal evidence; omitted resulting health is labelled inferred. Visibility departure does not imply death. A dead player who acts or is healed again counts as respawned or revived. Cash silver stays separate from estimated loot value. Only canonical silver pickups contribute cash; overlapping broadcasts remain supplemental. Omitted tax fields count as no tax for you and your party members alike. Reported healing is preserved without claiming effective healing.

**Combat Fame** and **Gathering Fame** are separate; Gathering Fame includes fishing, with a fishing breakdown in its tooltip. Fame awards match preceding Destiny Board progression on the same actor and reliable Photon stream, within four messages and 250 ms. The achievement reference's `killmobfame`, `gatherfame`, and `fishingfame` mission types identify the source independently of the active encounter. Missing or conflicting source evidence remains Unknown. Progress updates never create additional awards. The captured positive bonus is applied once to the award; cumulative player fame is retained only as detail. Existing awards with saved, valid components are corrected when their old calculation can be reproduced, but historical source categories are not guessed. PvP fame packet meanings remain unverified; player kills and encounter types cannot supply a fame amount.

Gathering and fishing packets can also report an additional progression component. When components 12/13 are valid, positive, equal, and the source is matched, one component is added to the ordinary award. This component already includes its applicable bonuses; applying premium or the reported bonus to it again would duplicate them. Captures with and without a reported bonus verify the resulting progression increment. Saved rows without these components cannot safely be reconstructed from ordinary fame.

Inventory requests create short-lived candidates. Your pickup is recorded when the server confirms the exact transfer into your inventory, or into a bag or equipment slot named by your own request; when a unique source decrement matches a gain in your inventory; or when the requested item leaves its loot container, which also covers stacks merged into existing inventory stacks. Banks, rearrangements and failed requests do not contribute personal/party earnings. Party chests attribute pickups to the player named by the loot allocation, and type-only removals to the single party member allowed to open that chest, unless your own pending request or an already recorded pickup explains the removal. A removal already explained by a pickup broadcast is not recorded again. Correlations follow capture time, and pickups still inside their correlation window are resolved before a map change, **Finish** or disabling recording. Pickups seen before the character and map are confirmed are kept outside sessions; party members among them are recognised from the party roster, which is kept across the first map join. Removals without a known recipient, and items missing from a public chest when it is reopened or emptied, are kept as inferred pickups by Unknown; they are shown and exported but not included in totals. Item names come from reference data whenever a pickup omits them. Loot is valued with the Black Market estimate when available, then the normal estimate; pickups without quality use the average of qualities 1-4, labelled in their tooltip. Gathering and fishing keep normal-quality estimates. Known estimates are frozen; missing prices may be filled later through the existing authenticated, batched EMV lookup and cache. Opening item history also requests missing estimates. Looter and looted-player guild and alliance come from the player directory, including players seen on earlier maps, and are completed for recent pickups when a player's identity arrives. Loot lists show them as Guild/Alliance (looter) and Source guild/Source alliance (looted player) columns, and loot search matches both. **Export loot…** on Current, a History session or Items exports exactly the loot list shown there, across all pages. History remains until explicitly deleted. Disable **Activity recording** to close current recording while keeping history readable; **Finish** permanently ends that session.

**Cleanup** shows Activity storage, the whole local database size, and session, encounter, event, and pickup counts. Preview removal of old history or loot picked up outside your party, then confirm deletion. Open sessions and original gathering records are kept. Deletion updates surviving totals and prevents pending amendments from restoring removed events. Freed SQLite space is reused; the file need not shrink immediately.

The journal batches writes every second or 250 changes. Database failures retain accepted work and visibly suspend recording if needed; the session records a gap, keeps the current map, character and inventory context, and resumes recording when writes are accepted again. A write the database rejects as invalid is logged and dropped instead of blocking later writes. A capture restart within a minute (adapter change or traffic recovery) records a gap and continues on the same map and character; after a longer stop, recording waits for fresh identity/map confirmation. When the server IP is not recognised (VPN or routing tools), activity is kept under the last known server, or server 0. Traffic silence is not logout. Process restart marks unfinished activity Interrupted. Shutdown drains recording before disposing capture/core services. Existing SQLite backups include Activity tables.

Debug builds log raw Activity packet probes when **Logs → Log verbosity** is **Debug** or **Verbose**. Probes cover every Activity subscription and inventory intent, plus resource identities, harvest/fishing lifecycle, and achievement/journal progression for fame-source investigation. Event, request, and response logs retain all supplied parameters, capture time, and transport identity; responses also include the result code. Information or higher stops packet probes.

The Activity schema is provided by the EF-generated `20261007145317_AddActivityJournal` migration. Existing startup database initialization applies pending migrations when the application is launched. Do not hand-edit migration files; generate future schema changes with EF Core commands.

### Gathering and fishing

Confirmed harvests from your character or party and confirmed local fishing rewards are recorded by Activity. They start or refresh the session inactivity deadline, but never start or prolong an encounter. Sessions receive Gathering, PvE, and PvP tags from the events visible in the selected scope; a session can have several tags. Fishing is part of Gathering and can also be filtered separately. Fame remains a packet award, and cash silver stays separate from all item estimates.

Fishing requires a successful completion response, a server reward, and a unique matching gain in your inventory. A completed catch is kept when you cancel, move items or leave the map right after it, catches that merge into older inventory stacks are recognised, and catches are settled on capture time so delayed processing cannot drop them. Shared inventory claims prevent the same acquisition from counting as both loot and fishing. Requests and ambiguous rewards are not personal earnings.

Existing gathering history is converted automatically at startup into closed Activity sessions with item summary records. The original gathering tables and rows remain untouched. Conversion preserves stored quantities, values, dates, and observed duration; it does not invent individual pickup times, map locations, fame, cash, or combat. Imported runs are labelled as summaries and have no temporal chart or encounters. Valid orphan checkpoints become partial Interrupted sessions; completed records take precedence. Conversion uses deterministic identifiers and durable import markers, so rerunning it does not duplicate runs or restore deleted Activity history. Invalid records and inconsistent totals are reported while the source data remains intact. This conversion uses the existing Activity schema and needs no additional migration.

## Private Mode and sharing

Private Mode saves all captured buy/sell orders, including loadout offers, to your
AFM account for the flipper and website price tools. No market-order categories
are also sent to AODP. Market history, gold prices and bandit events remain public.
Private Mode and both sharing choices default off and are saved in UserSettings.json.
All three survive restarts, sign-out and account changes. Turning Private Mode off
keeps the sharing choices saved for when it is enabled again. Sharing only applies
to private orders while signed in; private order uploads are skipped while signed
out, without a public fallback or an offline queue.

Settings > Sharing accepts a user ID, email or exact AFM website username in one
field. The website username is not an Albion character name. Both users must add
each other; up to five friends are supported. Ambiguous matches are rejected; use
email instead. Existing entries remain tied to the saved account if a username
changes. Private and shared price summaries expire up to 48 hours after their
five-minute observation bin starts. Turning sharing off affects new uploads only.

Release validation: enable Private Mode and both sharing choices, restart, sign
out and sign back in (also with another account); verify all choices stay selected.
Turn Private Mode off and back on, then disable each sharing choice and repeat
restart/sign-out checks to confirm both enabled and disabled values persist. Capture an
ordinary item and each former public exception (runes, souls, relics, alchemy
extracts and fish sauce), including loadout offers, and confirm no market orders
reach AODP. Verify history/gold/bandit uploads remain public. Check tooltips and the
Sharing field on a narrow window, and add/remove friends by all three identifiers.
These UI/capture checks require a signed-in client and live game traffic.
