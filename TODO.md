# DMU Helper TODO

## Fast Context

- Plugin name: `DMU Helper`
- Internal name: `DMUP4DebuffHelper`
- Repo: `Nainaiowo/dmu-p4-debuff-helper`
- Puni plugin ID: `162`
- Dalamud feed URL: `https://puni.sh/api/repository/nainai`
- Settings commands: `/dmu`, `/dmuh`, `/dmuhelper`
- DMU territory ID: `1363`

## Implemented

- [x] Initial plugin scaffold.
- [x] DMU-only party status scanning.
- [x] Helper window listing active party statuses, IDs, and timers.
- [x] Debug chat for gained/lost statuses.
- [x] Settings for helper visibility, font scale, background opacity, and debug chat.
- [x] Identified and tracked P4 debuffs:
  - `5545` Compressed Water
  - `5544` Forked Lightning
  - `5543` Cursed Shriek
  - `5546` Acceleration Bomb
  - `5548` Dynamic Fluid
  - `5547` Entropy
  - `4888` Black Wound
  - `4887` White Wound
  - `454` Allagan Field
  - `5464` Beyond Death
- [x] Added boss tell status tracking with `2056` params `1119`, `1120`, `1121`, and `1122`.
- [x] Added real/fake-aware assignment text that preserves the captured state until the debuff falls off.
- [x] Live helper shows the local player's P4 debuffs as compact icons with details on hover.
- [x] Added Buff Summary tab with current/recorded pull groups, boss tell history, and captured debuff records.
- [x] Added helper-window preview mode for inactive testing.
- [x] Combined P3 Black Hole and P4 debuff display into one compact DMU Helper window.
- [x] Uses BossMod naming for P3 `5454` as Primordial Crust.
- [x] P4 Calls section: one block per set with the spread element, accel STOP/MOTION per player, shrieks, and Inferno/Tsunami.
- [x] P4 settings: show/hide Active, Calls and Gazes; Calls can show only accel STOP players; players shown by full name, first name or job icon.
- [x] P1 Calls: Kefka's rings (stack/spread, ice, lightning), statue look away/look at, Support/DPS rocks, and East/West cleaves. Confirmed correct on live pulls.
- [x] Discovery logging (settings > Diagnostics) for mapping new mechanics: statuses, casts, NPC actions with targets, headmarkers, tethers, object animations and arena effects, with NPC positions and per-pull timers.
- [x] Verified the boss tell param mapping and every P4 real/fake call against BossMod replays (Sept 15 - Oct 3, 2026): no mismatches.
- [x] P4 Calls: personal GO BLUE / GO PURPLE Flood line.
- [x] P2 Forsaken calls: tower set count, "N BAIT - M CONE" style soak/bait call, buddy marker with SAME TOWER / SPLIT TOWERS, and PAST - BETWEEN / FUTURE - ACROSS. Buddy is picked in settings.

## Next Work

- [ ] Live-test the P2 Forsaken calls.
- [ ] Other P2 mechanics beyond Forsaken, if they need calls.
- [ ] P3 calls beyond Black Hole: same approach as P2.
- [ ] Mana Charge / Mana Release real/fake (on hold).
- [ ] Decide whether P1's early statue mechanic (Pulse Wave `47785` / Wave Cannon `47784`, about 75s in) needs a call.
- [ ] Decide whether discovery logging ships in public releases or stays a dev tool.
- [ ] Refine player-specific instructions after the group strat is chosen.
- [ ] Add screenshots after the helper has its real P4 behavior.

## Research Notes

Decoded from discovery logs and BossMod replays; IDs were stable across instances from Sept 15 to Oct 9, 2026.

- P1 Kefka (BNpc `19504`) ring headmarkers, odd = fake, even = real:
  - `673`/`674` stack/spread ring, applied to the player markers `128` (stack, 2 players) / `127` (spread, all 8). Fake flips the marker.
  - `675`/`676` ice floor ring. Fake = be in it, real = be out.
  - `677`/`678` lightning floor ring. Fake = be in it, real = be out.
  - Matching casts: real ice `47768`, fake ice `47771`+`47774`; real lightning `47775`, fake `47776`+`47777`; stack/spread resolves as Flagrant Fire III `47779` (stack) / `47778` (spread).
- P1 statue gaze: arena object `2015166` (real, look away) or `2015167` (fake, look at) animates `64` about 10s before Indolent Will `47796` / Ave Maria `47795`.
- P1 rocks: Graven Image (BNpc `19505`) tethers (id `45`) from the statue at `126.0, 41.5` mark the rock group; Vitrophyre `47792` lands about 10-12s later.
- P1 cleaves: arena object `2015164` animates about 5s before Gravitational Wave `47793` cleaves the west half (call East); `2015165` before Intemperate Will `47794` cleaves the east half (call West).
- P4 boss tells (status `2056`): Neo Exdeath `1121` fake / `1122` real; Chaos `1119` fake / `1120` real. A debuff's reality is the tell up when it is applied (Neo Exdeath's Grand Cross `47892` applies Water, Lightning, Shriek and Accel; Chaos's cast applies Entropy and Dynamic Fluid).
- P4 resolutions, even = real / odd = fake: Death Shriek `47894`/`47895`, Death Bolt `47896`/`47897`, Death Wave `47898`/`47899`, Death Surge `47900`/`47901`, Stray Flames `47906`/`47907`, Stray Spray `47908`/`47909`. Acceleration Bomb only fires Death Bomb `47893` when a player fails it; real bombs need roughly a second of standing still.
- P4 accel sets: a set's four bombs can be all STOP, all MOTION, or 2/2 (10 of 28 replayed sets were split).
- P2 Forsaken (Kefka `47804`): player headmarkers `715` stack (status `5084`, Spelldriver `47808`), `716` circle/defam (`5085`, Spellscatter `47809`), `717` cone (`5086`, Spellwave `47810`); status `5083` counts down soaks remaining.
- P2 towers: arena effects 1-8, two per set (set n uses spots n and n-2), shown with state 1; they resolve as the Path of Light `47806` 10.0-10.3s later. Buddy pairs holding a stack at the start soak sets 1-3 and 8, the others 4-7 (59 of 59 replayed sequences).
- P2 Future's End `47826` (ACROSS) / Past's End `47827` (BETWEEN): Kefka casts one during each even tower set. Odd sets have All Things Ending `47836`/`47837` cone baits.
- P4 Flood: the floods give a second Wound (`5541` White / `5542` Black) for the colour a player stood in. Beyond Death `1382` appears in the later Flood, where players already hold second-type Wounds.
