# Farming packet integration

Farming uses the normal typed `EventPacketHandler`, `RequestPacketHandler`, and
`ResponsePacketHandler` registrations in `Network`. The shared Join response and
Leave event handlers notify `FarmingTrackerService` directly. A typed Join request
starts transitions, while ChangeCluster and GetIslandInfos responses update island
metadata. NewBuilding and FarmableObjectInfo models decode object observations.

Harvest, finish, product collection, destroy, and pickup each have an explicit
request/response registration using the shared farming action models. The normal
response handler copies the server return code into `BaseOperation.ReturnCode`;
failed farming actions discard their pending correlation without recording a
pickup or removal. Feeding and nurturing continue to update through snapshots.

The tracker owns state, deduplication, pre-Join buffering, and upload coordination;
it no longer inspects raw packet dictionaries or routes packet codes. Packet models
reject unsupported data before updating the tracker. There are no broad farming
observers or requirements to register farming at the head of the handler chain.
Debug probes still forward packets through the existing chain as before.

Object handlers skip decoding while tracking is disabled, signed out, or outside
an island transition/visit. Missing object identities are rejected instead of
being interpreted as object zero. Failed Join responses leave shared player state
unchanged. Unknown operation codes are reported once per capture session.

The upload worker keeps the existing account-specific durable outbox. It streams
outbox JSON to and from disk, stops filling a batch when its byte budget runs out,
and removes acknowledged records by key. This keeps batch preparation and cleanup
from scanning the full offline backlog on every successful upload.

## Building lifetimes and tracker cleanup

Each visit has its own session-object cache. Join requests, local-player Leave,
cluster transitions, account changes and capture resets discard that cache.
A repeated Join response for the same session does not replay cached objects.
Ordinary object Leave evicts only the transient entry and never proves demolition.

Confirmed demolition timers are stored separately by server, island and stable
building UUID. They survive visibility loss, island/character/server transitions
and capture resets while the client keeps running. Disabling island tracking,
switching/signing out of the AFM account, or disposing the client discards them;
pending timers are not persisted across client restarts.

The September 16 captures establish the supported start sequence: a local
`BuildingChangeRenovationState` request with parameter 1 = 4, `NewBuilding`
parameter 29 = 4, and `FarmBuildingInfo` parameters 4/5 containing start/end UTC
ticks. A monotonic clock measures the full duration from the local request, so
server/client UTC skew does not expire a timer early. Cancellation requests and
building snapshots leaving state 4 disarm it. Cancellation can omit parameter 4
while retaining the old end in parameter 5; an end without a start is insufficient.

Once a confirmed countdown expires without an observed cancellation, the client
uploads a plot removal with `removalAssumed: true` and `occupantObservedAt` set to
the last building evidence supporting that countdown. It does so even when no
more packets arrive or the character has left. A visibility Leave after expiry
does not upgrade the assumption to a confirmed demolition.

An assumed removal hides the plot and its contents through the backend's read
filter. It does not retire its UUID, prune the outbox's children, or permanently
erase saved farmables. Fresh building metadata reverses the assumption. Real
observations later than the timer's supporting evidence win even if they were
captured before expiry and uploaded afterwards, including cancellation from a
different client. Genuine replacement or confirmed action removal still retires
the old building and clears its contents.

Farmables optionally include `plotObjectId`, the UUID of their owning building.
Metadata arriving before its parent is bound when that parent appears. Confirmed
replacement/removal retires the cached instance and contents, and the outbox
uploads plots before farmables even across batch boundaries.

Ordinary uploads retain schema version 1. Batches containing an assumed removal
use version 2; update the backend first. An older backend rejects version 2, leaving
the batch queued, instead of treating an unknown flag as a permanent retirement.
Once queued, assumed removals use the existing durable account-specific outbox.

The backend keeps manual-deletion cutoffs and confirmed retired plot UUIDs. Fresh
observations can rediscover manually hidden plots/islands; old queued observations
cannot. Historical ghosts without replacement/removal evidence still need manual
tracker cleanup. Pickup history is retained.

## Demolition and rebuild probe capture

Debug builds also probe candidate building lifecycle packets without interpreting
any of them as a confirmed demolition. Requests and responses include
`AttackBuildingStart`, `ActionOnBuildingStart`, `ActionOnBuildingCancel`,
`BuildingChangeRenovationState`, `ConstructionSiteCreate`, and
`TearDownConstructionSite`. Responses retain their return codes and raw parameters.

Events additionally include `Leave`, `AttackBuilding`, `ActionOnBuildingStart`,
`ActionOnBuildingCancel`, `ActionOnBuildingFinished`, `ConstructionSiteInfo`,
`NewBuildingBaseEvent`, `BuildingDurabilityUpdate`, and
`MiniMapOwnedBuildingsPositions`. Existing building and farmable probes provide
object identities, coordinates, and animal state for correlation. A `Leave` event
can also mean an object left visibility; it is not demolition evidence by itself.

For a capture, run a Debug client and select Debug or Verbose in the log viewer.
Record the times when demolishing and rebuilding a plot, then leave and re-enter
the island with the client still running. Keep some formerly occupied slots empty.
Inspect `Debug probe captured` entries in the daily log under
`%LOCALAPPDATA%/AFMDataClient/logs`. These probe additions do not change tracker state,
upload behavior, or Release builds.

### September 16, 2026 pasture demolition capture

The local log records a new pasture at `(145, 135)` at 16:50:26 (UTC-3), session
object 372. A goat appears at `(145, 129)` as object 373 at 16:50:51. Its
`PlaceableObjectPickup` request and successful response share request ID 2; a
`Leave` event for 373 arrives between them.

At 16:50:56, `BuildingChangeRenovationState` (operation 61) targets object 372
with parameter 1 equal to 4. The next `NewBuilding` retains that pasture's UUID
and changes parameter 29 from 0 to 4. `FarmBuildingInfo` supplies parameters 4
and 5 as UTC ticks exactly 60 seconds apart, consistent with this capture's
demolition countdown. At 16:51:56, an empty `MiniMapOwnedBuildingsPositions`
event and `Leave` for object 372 accompany the user-confirmed demolition. No
`ActionOnBuildingFinished` or `TearDownConstructionSite` was logged for it.

An unknown request with operation code 554 was rejected at 16:51:51, shortly
before the building disappeared. Its parameters were not recorded, so its role
is unverified. It did not appear in the subsequent cancellation/completion
capture. The temporary parser probe for 554 has been removed; unknown-code
routing remains unchanged. Ordinary visibility changes emit `Leave` too.

## Optional farming action timings

Adult-product progress is verified for the captured fed goats and sheep. The
September 9 wolf capture also validates the predicted 22-hour nurture cooldown
while food remains available. Product collection/reset and other timing conditions
still need confirmation. The client model, upload validation, Mongo storage, and
website support these optional fields inside an object's `state`. Packet parsing leaves these explicit action
fields unset; verified product progress is uploaded separately as described below.

| JSON field | Type | Meaning |
| --- | --- | --- |
| `productReady` | boolean or null | Products were ready to collect at observation time. |
| `productReadyAt` | UTC ISO date or null | Normalized time when products become ready to collect. |
| `nurtureReady` | boolean or null | Nurture was eligible at observation time; excludes the character's Focus balance. |
| `nextNurtureAt` | UTC ISO date or null | Normalized time when the next nurture becomes eligible. |

Missing and null both mean unknown. `false` means known not ready, not missing
information. A false flag can accompany a future deadline. `true` is ready now;
omit its deadline instead of sending a conflicting future time. A deadline can
also be supplied without a readiness flag. Once it passes, the website shows the
action as ready based on the last observation.

Deadlines must account for verified game timing, duration modifiers, and any food
or growth conditions that pause or block that action. Supply a deadline only when
it can be reached without another feeding or other prerequisite action. Otherwise
leave it unknown; the UI can still show a known unavailable flag and food status.
The website keeps those confirmed fields separate from its card's labeled
estimate based on `lastBoostAt` plus the catalog cycle length. See the September 9
captures below. Calculated estimates never populate the confirmed upload fields.

## September 9: feeding and nurturing after prolonged hunger

At 00:04-00:05 local time, Expert's Foal (entity 450) and Master's Foal
(entity 471) were fed and nurtured for the first time. Their snapshots change
field 13 from undefined to the action timestamp and field 14 from 0 to 1.
Expert's Ox Calf (entity 443) is fed at 00:05:29 and nurtured at 00:05:34,
changing count 1 to 2. Screenshots confirm all three require feeding first.

The calf's previous nurture is September 7 at 03:10:33 UTC. Its growth anchor
before feeding is only 4:39:48 later; the intervening hunger lasts about 43:15.
The successful second nurture occurs 47:54:59 after the first. This supports a
wall-clock cooldown continuing during hunger, rather than 22 fed hours required
after each nurture. It does not establish the precise eligibility boundary.

The website therefore estimates last nurture + the item's catalog interval
(22 hours for these animals), without the Premium growth multiplier, and labels
the result as estimated. Food must still be present before suggesting nurturing.
First nurture requires food; completed nurture counts have no next timer. Animals
expected to mature before the next cooldown instead say so. Missing timestamps
remain unknown. The later wolf capture below validates this prediction near the
22-hour boundary while food is available.

## September 9, 2026: wolf nurture cooldown boundary

The Caerleon Greywolf Pup (session entity 265) has two recorded nurtures. Its
`FarmableObjectInfo` field 13 is `639244835286819525`, meaning the previous nurture
occurred on September 8 at 13:58:48.682 local time (UTC-3). The catalog interval is
79200 seconds, so `lastBoostAt + interval` predicts September 9 at 11:58:48.682.
The animal's 0.5 Premium growth multiplier does not halve this cooldown.

Screenshot file times and the user's observed countdown agree with that deadline:

| Local time (UTC-3) | Predicted time remaining | Observed state |
| --- | --- | --- |
| Approximately 11:56:07 | 2m 41s | Website shows 3m; game says already nurtured. |
| Approximately 11:57:34 | 1m 15s | Website shows 2m. |
| Approximately 11:58:57 | Deadline passed by about 9s | Game enables the red Nurture button. |

At 11:59:05 the logs record request 291 (`BoostFarmable`), a 603 Focus deduction,
event 201 (`FarmableObjectInfo`) changing field 14 from 2 to 3 and updating field
13, and completion event 321 (`BoostFarmable`). The new server action timestamp
is 11:59:04.807; the client logs a successful object upload at 11:59:08.845.

No separate nurture-unlock packet is recorded at the predicted boundary. The
capture supports deriving the countdown from the last nurture and catalog
interval; the usual action snapshot confirms the resulting nurture count. It
does not identify an additional packet field for explicit readiness.

The wolf still has about ten hours of food when the button unlocks, and no new
feeding is needed at this transition. Although the game headline says "hungry,"
its food and growth timers continue advancing. This capture therefore validates
the fed-animal cooldown, not an unlock while food is exhausted. Keep food
eligibility checks and the separate hunger/production verification tasks. Other
animals must use their own catalog intervals rather than a hard-coded 22 hours.

No timer correction is needed for this case. The current card calculation stays
in place; `nurtureReady` and `nextNurtureAt` remain unset by packet parsing until
their normalized semantics are verified for the relevant conditions.

## Completing the packet mapping

1. Compare `FarmableObjectInfo` with in-game adult-product and next-nurture status
   before and after feeding, collection, nurture, and the next eligible cycle.
   Fields 10/11 are verified as accumulated production seconds (fixed point) and
   their UTC timestamp for fed goats/sheep. Collection/reset and pauses during
   an unfinished adult production cycle still need verification (see below).
2. Normalize verified observations into the four fields when constructing
   `FarmingObjectState` in `FarmingTrackerService.ObserveFarmable`. Use existing
   conversion helpers, and keep unsupported observations null. Do not carry a
   previous cycle's deadline into a new snapshot.
3. Confirm that normalized deadlines match the displayed game timer, including
   paused conditions and re-entering an island. If a mapping needs item duration
   data to calculate a deadline, use the verified item data rather than assuming
   all items share a cycle length.

The shared contract and UI do not require the original packet field numbers.
Normalizing the result in the client keeps subsequent mapping changes there.
No new collection, endpoint, subscription rule, or schema-version bump is needed.

## Verified product progress and display

Farming snapshots include optional `hasPremium` from the existing player Premium
state. Join initializes that state before buffered farming observations are
uploaded, including observations received before their building metadata. Older
snapshots remain unknown. This is the observing character's status at the visit,
not the island owner's status or a guarantee about Premium at collection time.
The website reuses the calculator's local bonuses and expected-yield formula;
animal growth bonuses continue to use the recorded duration multiplier.

The client now reads a single field-10/11 pair into optional
`productProgressSeconds` (fixed-point seconds normalized with the existing helper)
and `productProgressUpdatedAt` (UTC). Other product array shapes remain unknown.
The backend validates and stores both fields; older clients remain compatible.
Collection clears old progress and deadlines unless a newer snapshot has already
arrived. No next-cycle restart time is assumed.

The website uses its existing grown-item relationship to resolve an adult animal
when a full snapshot has no baby-growth clock, zero baby growth, and no mature
baby flag. It uses the adult's existing food and production catalog data, so adult
cards do not show baby durations or nurture counts. This also fixes names and food
timers for already stored observations; product countdowns require new observations
from the updated client and backend.

Production countdowns are limited to the verified single-product, multiplier-1
case. The website caps extrapolation at food exhaustion and only exposes a
collection deadline when the available food can cover completion. A partial feed
shows production remaining and the need to feed again. These are estimates based
on the last observation; paused and post-collection captures are still needed.

Plot slots use the game's screen projection `(x + y, x - y)` and stay in a diamond
layout on narrow screens through horizontal scrolling. Unknown slots are not
treated as empty.

## Island map layout

`ChangeCluster` response parameter 1 identifies the island layout, for example
`ISLAND-PLAYER-SWAMP-0001c`. The client uploads it as optional island metadata
`layoutId`, separately from the island UUID and `homeCluster` city. Join packets,
travel-list updates, and pending-upload coalescing preserve an observed layout.
A newly observed layout replaces the old one, including after an island upgrade.

The backend validates, stores, and returns `layoutId`. The website looks it up in
the existing map catalog and projects plot coordinates using the map's authored
bounds. Unknown layouts or unavailable images retain the relative overview.
Re-enter an island with the updated client to record its layout; no migration or
backfill based only on the home city is needed. The numbered map marker and its
detail card share the same selection.

Direct login captures have no verified layout identifier. Leave and re-enter the island to capture the verified `ChangeCluster` layout; the backend preserves previously recorded layouts across direct logins. The experimental login-port reader and metadata probes have been removed.

## Snapshot and action behavior

- A newer full farming state replaces the previous state. Omitted timings clear
  old values, including when an older client uploads a snapshot. Metadata-only
  building observations continue to preserve the same occupant's state.
- Confirmed product collection clears old product timings if no newer snapshot
  has arrived since the request. It does not assume a new production cycle or
  erase a newer snapshot that already describes that cycle.
- Captured nurture actions send a fresh `FarmableObjectInfo` immediately before
  the `BoostFarmable` completion event. Map the full snapshot. Do not route this
  event through the harvest/product response correlator: its packet shape differs.
- Removal and replacement continue to clear the old occupant. Pickup history
  stays independent of these optional timers.
- Existing uploads remain schema version 1. Null client fields are omitted by
  the existing serializer, and the existing byte limit still applies.

The website uses its existing status cards and countdown formatting. Product
readiness contributes to collection summaries; nurture readiness is displayed
separately. Missing timing data retains the current unknown status.

## September 8, 2026 capture findings

These observations compare local probe logs around 13:57-13:59 (UTC-3), the
corresponding game screenshots, and the local item definitions. These are
historical findings; later follow-ups above resolve the fed-wolf cooldown boundary.
They do not enable any additional packet mapping.

- **Second wolf nurture:** the screenshot shows a hungry wolf with 45:53:18
  growth remaining and Nurture 1/3 disabled. Feeding restores food and enables
  nurture. The successful nurture costs 624 Focus, updates field 13, and changes
  field 14 from 1 to 2, matching Nurture 2/3 and 20% offspring chance. The growth
  counter before feeding is 86802 seconds, matching the displayed remaining
  growth after the catalog duration modifier.
- **Next-nurture eligibility:** food is a prerequisite in this sequence, even
  though the character has enough Focus. The second nurture occurs about 37
  hours after the first, with about 24 hours of accumulated growth; both exceed
  the catalog's 22-hour cycle. This does not distinguish elapsed-time cooldowns
  from growth-based cycles or establish the exact eligibility boundary. Do not
  derive `nextNurtureAt` from this capture alone.
- **Gosling:** feeding and its first nurture are captured. Field 14 becomes 1,
  Focus decreases by 642, and the screenshot's offspring chance changes from
  80% to 120%. The catalog allows one nurture cycle, so there is no further
  nurture to schedule for this gosling. Food and growth calculations agree with
  the screenshot's approximately 22-hour timers, including their 3-second gap.
- **Crop pickups:** successful `FarmableHarvest` responses contain 2 carrot
  seeds + 12 carrots, 1 corn seed + 6 corn, and 1 potato seed + 12 potatoes.
  The carrot quantities match the screenshot. The client logs successful pickup
  uploads. Newly planted corn then changes to watered, with a 525 Focus cost.
- **Adult-product candidates:** the placed goat and sheep have no animal-growth
  fields 1/2, but do have field 10 `[0]` and a timestamp in field 11. Their food
  is also zero. These are the only populated 10/11 samples in this capture;
  neither animal is subsequently fed and no `FarmableGetProduct` operation is
  captured. The field scale, advancing progress, readiness, and collection reset
  still need verification.
- **Adult identification gap:** item definitions for grown goats, sheep, and
  geese reuse the corresponding `..._BABY` tile. `NewBuilding` field 3 identifies
  that tile, so it cannot alone determine the animal's current life stage. The
  current client stores that name and the website classifies from its item
  definition, which can classify an adult as a baby. Resolve this along with the
  product mapping; do not rename every baby tile to its grown item. Field 2 is
  a tile index, not an interchangeable inventory item index.

At this point the next useful captures were adult panels across feeding,
production, and milk collection, plus a fed animal crossing its nurture deadline.
The follow-ups below verify advancing production; September 9's wolf capture
above supplies the requested nurture-boundary observation. Adult collection/reset
remains a separate verification task.

### Follow-up at 14:08: adult feeding

The new screenshots explicitly identify the placed animals as Goat and Sheep,
first hungry and then producing milk. This confirms the adult tile-name problem
for these two objects; their building names still end in `_BABY`.

- Goat feeding succeeds with 12 carrots and 6 corn. Sheep feeding succeeds with
  9 potatoes. Field 8 reaches `8640000` (864 nutrition) in both cases, and the
  client logs successful object uploads.
- Field 11 changes from the placement timestamp to the feeding timestamp, equal
  to the new food anchor in field 9. Field 10 remains `[0]`. This supports a
  product progress/anchor pair, but zero-only samples cannot verify field 10's
  units or establish how later snapshots account for elapsed production.
- The catalog production duration is 79200 seconds (22 hours), matching both
  screenshots. Field 12 stays at 1. The screenshots show Premium increasing
  product yield; do not halve this duration using the baby growth modifier.
- Full food lasts `864 * 91.67 = 79202.88` seconds, about 3 seconds longer than
  production. This matches the displayed food/production timer gaps: goat
  21:59:59 / 21:59:56 and sheep 22:00:00 / 21:59:57.
- No later snapshot of either producing adult or `FarmableGetProduct` operation
  appears in the log through 14:10. To verify advancing progress without waiting
  for completion, leave and return to the pasture's visibility range (or re-enter
  the island), then capture the production panel. Collection and the next cycle's
  reset still need a later capture when the milk is ready.

### Follow-up at 14:11: advancing adult production

Fresh building observations identify the same goat and sheep by their persistent
object IDs. Their `FarmableObjectInfo` snapshots now establish the units of the
product progress/anchor pair:

| Animal | Field 10 | Production seconds (`/ 10000`) | Field 11 advance since feeding |
| --- | --- | --- | --- |
| Goat | `[1930000]` | 193 | Exactly 193 seconds |
| Sheep | `[1720000]` | 172 | Exactly 172 seconds |

Field 11 is a timestamp for the accumulated progress, not the original cycle
start or the completion time. For these single-product, continuously fed animals,
the completion estimate is `field11 + (catalog production seconds - field10 / 10000)`.
The modifier is 1 in these captures; other modifiers remain unverified for adults.
Use the existing fixed-point and UTC conversion helpers when implementing this.

Both snapshots preserve the completion estimate from initial feeding exactly:
September 9 at 15:08:30.689737 UTC for the goat and 15:08:50.3858503 UTC for the
sheep (12:08 local, UTC-3). Food updates independently: the goat has 862 nutrition
and the sheep 863, with their food anchors advanced by the consumed nutrition's
duration. In both cases the food deadline still falls 2.88 seconds after product
completion, so these observed cycles have enough food to finish.

This is sufficient evidence for the normal fed-animal countdown in these cases.
No `FarmableGetProduct` operation or completed product snapshot was captured.
Keep collection/reset and unfinished production pausing for lack of food as
separate verification tasks; next-nurture timing is unchanged. At capture time the
parser did not yet read fields 10/11; the implementation above now uploads them.

### Follow-up at 14:21: partially fed adult goose

A newly placed adult goose receives one potato in a successful `FarmableFill`
response. Food becomes `480000 / 10000 = 48` nutrition. Its first production
snapshot is zero; a subsequent snapshot contains field 10 `[340000]` and field 11
advanced by exactly 34 seconds. This confirms the same progress mapping for the
captured goose egg cycle.

The two screenshots show production remaining 21:59:50 and 21:59:17, with food
remaining 01:13:10 and 01:12:37 respectively. These match 10 and 43 elapsed seconds
against a 22-hour production duration and `48 * 91.67 = 4400.16` food seconds.
Both screenshots nevertheless say "Goose is hungry." That headline does not
establish exhausted food or paused production: progress is visibly advancing.
The reason for the game's label remains unverified.

Without additional feeding, the food deadline is September 8 at 18:34:56.0998797
UTC (15:34:56 local, UTC-3). There is only about 1:13:20 of food for a 22-hour
cycle, so do not publish a product completion deadline from this snapshot. If
production stops at food exhaustion, about 20:46:40 should remain. Verify this
prediction with two refreshed snapshots a few minutes apart after food runs out,
then capture another snapshot after feeding to verify resumption. No adult
production pause or resumption has been observed yet in this sequence.

### Follow-up at 14:22-14:23: another wolf's second nurture

The screenshot sequence matches a different wolf from the one nurtured at 13:58.
Its persistent object identity also matches the first nurture captured on
September 7. Before feeding it has 86802 accumulated growth seconds, zero food,
and one nurture; the calculated remaining growth is exactly the displayed
45:53:18. Feeding consumes 29 T5 meat and restores 1488 nutrition, enough for
24:06:42 of growth with the observed 0.5 duration modifier. Nurture becomes
available, then costs 624 Focus and changes field 14 from 1 to 2 while field 13
updates. Subsequent snapshots retain that nurture state; uploads succeed.

There are 37:30:09 between this wolf's first and second nurtures, with 24:06:42 of
growth accumulated before feeding. Both candidate 22-hour eligibility measures
are already satisfied, so this repeats the food prerequisite confirmation without
resolving the timer. The observation missing at this stage was nurture becoming
available while the animal remains fed, without feeding at that transition; the
September 9 wolf capture above now supplies it. A separate wolf also receives its
first feed/nurture at 14:23:34 (624 Focus, count 0 to 1).
