# Conversion rules (agreed with the project owner)

## Goal and hard constraints
- Output is `.sav` files only (`Anstoss2\DATA.DEF` and `DATA.VER`, mirroring the install). No binaries are edited,
  no nation records are renamed, added or removed.
- No original Anstoss 2 data may remain: every record is replaced with FM data.
- Every file keeps its exact record counts (teams, nations, stadiums, referees, name pools, ...), because the game
  expects them. These are database files: all content is loaded regardless of the main league the player picks later.
- Text limits: player last name 1-15 (the editor says 16, but the game shows garbage after a 16-character surname), first name 1-9 characters; stadium name 3-28. Strength 1-7, age 17-50.
- Players per team: shipped data uses 16-30. Keep inside that range (configurable).

## Nations
FM nation -> A2 nation by an explicit, editable table. Nations A2 does not have (Serbia, Montenegro, Bosnia, ...)
map to "Sonstige" (id 0).

## League size mismatch (cascade rule)
A2 league sizes are fixed (e.g. Bundesliga 18, Premier League 20). When the modern FM league has a different size:

- **FM league too big** (e.g. FM 20, A2 18): the clubs with the lowest reputation are moved one tier down.
- **FM league too small** (e.g. FM 16, A2 18): the highest-reputation clubs of the tier below move up to fill the gap.

Implementation: process the tiers of a country top-down. `pool(t)` = FM clubs of tier t + clubs pushed down from
tier t-1. Take the A2 league's slot count from `pool(t)` by highest reputation; the remainder is pushed to tier t+1.
If `pool(t)` is smaller than the slot count, pull the highest-reputation clubs from tier t+1. Overflow below the last
A2 league goes to the spare clubs (clubs of the Land file that are in no league), then to the 40 tail club names.
Only clubs that have a full squad in the FM save can fill team slots.

## Player attribute mapping (configurable)
How FM player data becomes Anstoss 2 player fields is driven by user-editable settings (an Options screen, stored in the
project as `mapping\settings.json`, with sensible defaults), not by hard-coded numbers:
- **Strength (Staerke) is 1-7 and is derived from FM Current Ability (CA).** Never 8: 8 is a random in-game event and a
  database file must not contain it. Default: CA bands -> 1..7 (editable thresholds).
- **Abilities and traits** are rules of the form "FM attribute(s) + threshold -> A2 ability", e.g. Pace >= 18 ->
  Schnelligkeit. A field player may have at most two positive abilities, so when more rules match, the strongest two win.
  Negative abilities, the main character trait, the extra trait and health flags use the same mechanism
  (FM personality attributes, traits, injury proneness as inputs).
- Position(s), skin, hair, age (clamped to 17-50), nationality (via the FM nation names + a static A2 table) and name
  shortening (15 / 9 characters) follow fixed rules in code.

## What the writer produces (src/A2G.Convert/Writing)
Output: `<project>\Anstoss2\DATA.DEF` and `DATA.VER`, first seeded with a full copy of the original folders, then the
generated files replace the originals (both folders get identical files). Record counts never change.

Generated from FM data: `Land*.sav` (team names, players), `LIGA*.SAV` (team ids; the fixture tables are slot-based and
stay valid), `Stad*.sav` (names), `Avereine.sav` (130 clubs x 11 players, 40 spare clubs, colours/capacity/finance derived),
`JUGEDEUT/ENGL/FRAN/ITAL/SPAN.SAV` (name pools from FM players of the nation).

Placeholders, because FM saves do not contain the data: city = place word taken from the club name, nickname = city,
stadium name = "<city> Stadion", Avereine kit colours = deterministic from the club id, capacity from reputation.
The stadium shape (section layout, facilities) and the trainer / staff blocks come from the original slots, paired by
stadium size (bigger stadiums go to higher-reputation clubs); the section capacities are rescaled to the club's capacity.

Squads (src/A2G.Convert/Engine/PlanBuilder.cs `SquadSelector`, positions from `PositionRules`): picked by A2 main position, not just by
ability. Players under 17 are never picked (Anstoss 2 players are 17 to 50).
- League teams (and spare teams): 2 goalkeepers, 1 libero (only if the club has one, otherwise a third offensive midfielder),
  4 central defenders, 2 left and 2 right backs, 2 defensive, 2 offensive (central), 2 left and 2 right midfielders, 2 strikers = 21.
  Quotas are settings (`SquadQuota`); a squad size above the total adds the best remaining players (goalkeepers and
  strikers capped), below it drops the weakest of crowded positions.
- Non-league clubs (Avereine): exactly 11 = 1 goalkeeper, 2 central defenders, 1 left and 1 right back, 1 defensive, 1 offensive (central),
  1 left and 1 right midfielder, 2 strikers (`AvereineQuota`).
- Ranking inside a position: pick score = CA + weight x (PA - CA) x youth factor (full up to age 21, fading to 0 at 27;
  weight 0-100 %, default 50 = a 100/170 talent ranks like a 135 player). Presets: current ability only / balanced /
  potential first.
- A missing position is filled by the club's own players: first those who list the position as a secondary position, then
  the substitution chain (no striker -> offensive, right, left midfielder; no libero -> offensive midfielder; no left back ->
  left midfielder, right back, central defender ...), then anybody else of the club.
- Only when a club still has fewer than 16 players or fewer than 2 goalkeepers (an FM save loads only some leagues
  completely, so lower-league squads are small) the free pool of the club's nation tops it up. This is a data-gap
  workaround, not a feature: Anstoss 2 has no free agents, the players just join the team. Free FM players stay
  available for manual edits.
- Manual edits (Teams tab, player picker with a vertical pitch, CA / PA filters) respect the limits: 16-30 players (11 for
  non-league clubs). A player is in one squad only: adding one who is in another squad moves him and is refused when
  that squad would fall below its minimum; a full squad takes the new player in place of the selected squad player.

People (from FM staff, `staff.csv`; FM26 saves do not label jobs or store coaching attributes, so these are rules, not facts):
- Each team's trainer block = the best-rated staff member of that club (age >= 30 preferred), written surname first, with
  strength scaled from staff CA (100 -> 2 ... 180 -> 14), type from the coaching preference "hardness of training"
  (>= 15 = Driller, otherwise spread over the other types) and age (20-90).
- Per country the best-rated unused staff of the nation fill, in this order, `Trai*` (40/40/40/20/20, first name first,
  strength 2-14, type, age), `TTra*` (20, rating 5-14), `Mana*` (40/40/40/20/20, rating 1-15), `NSch*` referees (40,
  competence 1-10 from CA, hardness 0-10 from the coaching preference) and `Prom*` (20 prominent persons, favourite club =
  one of the first league's teams, chosen deterministically).
- If a nation has too few staff, the remaining records keep the original data and a warning is shown.

Name pools: the five national pools `JUGE*` and the twenty youth pools `JUGEND1-20` are filled with the names of FM players
of the nations each pool serves (the editor help lists them: 1 Africa, 2 Arab states, 3 Asia, 4 Belgium, 5 Bulgaria,
6 Denmark/Faroe, 7 Finland, 8 Greece/Cyprus, 9 Holland, 10 Israel, 11 Norway, 12 Poland, 13 Brazil/Portugal, 14 Romania,
15 Baltics/Russia/Ukraine/Georgia/Moldova, 16 Iceland/Sweden, 17 Albania/Croatia/Slovenia, 18 Slovakia/Czechia, 19 Turkey,
20 Hungary), topped up with FM staff names of the same nations when the save has too few players of them.

Still original (no FM source): sponsors, ISCHIRIS, Kunden (DATA.VER), Fangesa, the unknown numbers of the 21-number staff block
(only the kits [2-7], the season results [17] and the player count [20] are written) and the stadium numbers of each team (apart from
the rescaled capacities), the Land tail person. KLEINIG only changes the start year; the Avereine country triplets are written
(champion and cup winner per nation, see "Results of the last season").

`SavValidator` checks the output against the original structure and all documented limits (names, 16-30 players,
strength 1-7, ages, positions, nationality indexes, league ids) and `A2G.Sav.RoundTrip` re-parses every output file.

## UI requirements
- **Modern, slick look and feel** (the first window was a rough starting point): clean light theme, cards, flat
  buttons, a top tab navigation, readable typography.
- **Player inspector:** inspecting an FM player shows the Football Manager profile (attributes, positions, personality,
  CA/PA) on the left, an arrow, and on the right a replica of Anstoss 2's "Spielerinfo" window showing what the converted
  player looks like: name, club, flag (FLAGGEN\GROSS\<code>.BMP), age, strength, main and secondary positions
  (HP / NPs), abilities, character text, portrait. Values the game computes at game start (market value, salary, form,
  statistics) are shown as "calculated in the game". It uses the current settings, so changing them updates the preview.

## Manual editing (required UI feature)
The automatic result is a `ConversionPlan` (saved in the project folder), not final files. The UI lets the user:
- swap two teams between league slots, or swap a league team with a team from the pool of leftover FM clubs
  (swaps keep every league at its fixed size);
- move players between teams, or between a team and the pool of free FM players, within the squad limits
  (16-30 players, at least 2 goalkeepers) and name limits;
- see live validation (record counts per file, squad sizes) before writing.
User edits are stored as overrides keyed on FM's stable club and player ids so they re-apply when the FM export is
refreshed. The `.sav` files are written from the plan as the last step.

## German Regionalliga (tier 3 = four parallel groups of 18)
The FM save does not expose club locations (only a numeric `city_id` without names), and a hand-maintained region
table would have to be updated for every new FM release. So no regions are used:
1. The cascade fills the 72 tier-3 slots purely by reputation (FM 3. Liga plus the best FM tier-4 clubs).
2. The 72 clubs are dealt into the four groups (Nord, Ost, Sued, Suedwest = LIGARLNO/OS/SU/SW) in a snake order by
   reputation (1->Nord, 2->Ost, 3->Sued, 4->Suedwest, 5->Suedwest, 6->Sued, ...), so the groups are equally strong
   and the result is deterministic.
A geographic split could be added later as an option if fmsave ever exposes club locations.

## Traits, characters and health states
Rules live in `settings.json` (`TraitRules`, `MaxTraits`) and are edited on the Options screen ("Traits & character").
Each rule is a list of conditions (an FM attribute, FM personality value, or age / CA / PA / PA-CA between "at least" and
"at most", 0 = no limit); a player earns it when every condition holds. Bits are the verified .sav values
(docs/Dev/sav-format-verified.md): traits 1..1024 plus Talent 2048, characters 2..256 (one per player), health states
512 / 1024 / 2048 (one per player, same column as the character).
- At most `MaxTraits` traits (default 1; best margin first, exclusive pairs respected), the Talent flag comes on top.
- Goalkeepers only get the first five traits (the game does not show more) and never Talent.
- Defaults come from `TraitEngine.DefaultRules`; the thresholds were chosen from the FM26 test save to land near the
  rarity of the shipped data (about 27% with a trait, 17% with a character). Musterprofi needs PA >= 170 on top of
  Professionalism >= 16 (it matters for the random strength-8 event). Geldgeier: low professionalism, low
  sportsmanship, low loyalty and high determination. Knieprobleme: injury proneness >= 18.
- The Options screen shows per rule how many players of the loaded FM save meet it (after the plan was built).

## Positions
A2 has ten positions (file values): 1 Torwart, 2 Libero, 3 Manndecker, 4 linker / 5 rechter Verteidiger, 6 defensives
Mittelfeld, 7 linkes / 8 rechtes Mittelfeld, 9 offensives Mittelfeld (the game's name; every position in the middle of the midfield except the defensive one is an offensive midfielder: FM MC and AMC), 10 Stuermer. FM ratings used: 1 GK, 2 SW, 3 DC, 4 max(DL, WBL), 5 max(DR, WBR), 6 DM,
7 max(ML, AML), 8 max(MR, AMR), 9 max(MC, AMC), 10 STC. Main position = best rating (ties: GK, ST, 9, 6, 7, 8, 4, 5, 3, 2),
two secondary positions at rating >= 15 (setting). Left/right (7 vs 8, 4 vs 5) is not verifiable from the data.

## Popups in the Teams tab
- Squads are edited in the team popup: the squad by position, a "Reserve" view with the club's other FM players (add to / remove
  from the squad), and "Find other players..." for free FM players of any club. The Teams tab itself only lists the teams (with a
  search on both sides) and swaps them.
- Double-click a player (squad list, reserve list, search results) for the conversion preview: the FM profile and the converted
  Anstoss 2 player info, exactly as in the Players tab.
- Double-click a team (either list, also a club of the pool) for the team information: nation, league, how the name is
  written, FM reputation, average CA / PA / age, the squad list and a vertical pitch showing how many players the team has in
  each of the ten Anstoss 2 positions (green = wanted number reached, red = players missing, blue = more than wanted).
- City, nickname and stadium name are not in an FM save, so they are generated from the club name (city = place word of the
  name, nickname = city, stadium = "<city> Stadion"). The team popup lets the user change them; they are stored per FM club
  id in `ConversionSettings.ClubInfo` (settings.json), survive a rebuild, are written by `ClubTexts` and are kept when the
  options are reset to defaults. Stadium capacity is editable too (3,000-110,000; popup stepper, `Capacity` column in the
  club info file): default from reputation, `3000 + 62000 * x^2` with `x = (reputation - 1000) / 8000` clamped to 0..1,
  rounded to 100. In Stad*.sav the original slot's 24 section capacities (ints 0-23) are rescaled to the target (multiples
  of 100, exact sum, at most 13,500 per section); slots are paired with clubs by the template's real total capacity.
  Avereine clubs get the capacity directly.
- Every player and team row in the Teams tab (and the picker and team popup lists) has a magnifying glass that opens the same
  popup as a double-click; double-click keeps working.
- The FM side of the preview also shows FM's hidden attributes (consistency, dirtiness, important matches, injury proneness,
  versatility). A preview opened from a team has arrow buttons (and the left / right arrow keys) to cycle through the players
  of that team.
- Club information (city, nickname, stadium name) of every club of the plan, all nations, can be exported as a semicolon
  separated CSV (UTF-8 with BOM, opens in Excel) with "Export club info..." in the Teams tab and imported again with
  "Import club info...": clubs are matched by the ClubId column, only City, Nickname and Stadium are read, an empty cell or a
  text equal to the generated default means "default", unknown ids are counted and skipped.
- The club information export now writes a formatted Excel workbook (.xlsx, `ClubInfoXlsx`) by default; CSV is still possible
  (choose it in the file dialog, or import a CSV). The workbook needs no import step in Excel: dark frozen header row with a
  filter, read-only columns (ClubId, Nation, League, Club (FM), Written as) in grey, the editable columns (City, Nickname,
  Stadium) in yellow with a text length check (16 / 18 / 26), sensible column widths, and an "Info" sheet with instructions in
  the application language. IDs and names are stored as text. Reading also accepts files saved by Excel (shared strings, rich
  text). Verified by opening the generated files in Excel (426 rows for the test save), editing a cell, saving and importing.

## Crests
Because the game picks the crest by the team's slot, a club in a reassigned slot would show another club's badge. The writer
generates a 32x32 crest per club (`CrestWriter`: round badge, club-specific colour pair and stripe pattern, 2-3 letters from the
significant words of the written name) into `DATA.DEF\WAPPEN` and `DATA.VER\WAPPEN` of the output. A slot that keeps its original
team takes that team's original crest to its new position. To use other crests, replace the `VW_*.BMP` files (32x32, 8-bit).

## Own crests (crest folder and crest manager)
The project folder has a `Crests` folder (button "Crest folder" in the Crests tab; created with a README.txt and `_club-ids.csv`, the FM club ids).
An image named by the FM club id (`1234.png`, `1234 - Bayern.bmp`, any size, png/bmp/jpg/gif) is scaled to 32x32 and reduced to 256 colours
(`CrestLibrary`) and used instead of the generated crest; a 32x32 8-bit BMP is used unchanged. `WAPPEN\Crests.md` shows the source of every crest.
The Crests tab (step 6) browses the FM clubs by nation, imports an image, opens a 32x32 pixel painter or removes a crest; it saves `[club id].bmp`
into the Crests folder. Only clubs of the five main nations have a crest in the game (slot = position in the Land file).

## Skin and hair
An FM save (and fmsave) has no appearance data. Defaults (`PlayerLook`): skin 0/1/2 (light/medium/dark) from the player's nationality using a share per nation
(e.g. Ghana 95% dark, Brazil 35/40/25, Japan 90% light; unlisted nations light; the same player always gets the same result); hair 0-5 (very light blond, golden blond, brown, red, black, bald; order checked in the game) from skin and age (a few players from age 35 are bald; the game has no grey hair). The setting `SkinFromNationality` (settings.json, default on) switches the rule off (all light).
In the Players tab and the player popups the user can change skin and hair per player (stored by FM player id in `PlayerLooks`; values equal to the default are not stored;
"Default" undoes it). The overrides survive a plan rebuild and "reset to defaults".

## Kits
FM saves have no kit data. Every club gets a made-up default kit (`Kits.Default`: a pleasing pair of colours from the club id, mostly plain or hooped, a white or contrasting away kit; home and away shirt
colours always differ). The team popup shows both kits (drawn from the game's 13 colours and 7 stripe patterns) with a stepper for every part; "Save" stores a changed kit per FM club id (`ClubInfoOverride.Kit`),
"Use defaults" removes it. The numbers are written into the Land staff block (ints 2-7) and the Avereine record (so the slot's original kit is no longer kept). The generated crest uses the home kit colours
(band = shirt, caps = shorts). The club information file (Excel and CSV) has eight kit columns (home / away pattern, shirt, shorts, socks) as numbers; in Excel the cells take the colour of their number and the sheet Kits lists the numbers. Empty or invalid kit cells keep the current kit.

## Shared library

Club information (city, nickname, stadium, capacity, kit) and crests are also kept in a per-user library, `%APPDATA%\A2G-SAV-Converter\Library` (`clubs.json`, `Crests\`), keyed by FM club id. A project that is opened for the first time gets the library content for every club it has no edit for; afterwards every save feeds the project edits back (resetting a club removes it from the library). `Config.LibraryApplied` in project.json marks the first adoption.

## Results of the last season (flags)
The game keeps the last season's results per team: in a Land*.sav the number at index 17 of the team's staff block, for the
non-league clubs the two numbers after the country in the 30 triplets at the end of Avereine.sav. The converter writes them;
the original values of the slot are not kept.

| Value (lowest 3 bits) | Meaning |
|---|---|
| 1 | champion |
| 2 | UEFA Cup place |
| 3 | relegated (shown in the lower league) |
| 4 | promoted |
| 6 | runner-up |
| +64 / +128 | cup winner / cup finalist |

(Decoded from the original data and the game editor's legend; relegated and runner-up are inferred from where they occur and still
to be confirmed in the editor. Other bits, e.g. 16 in the Regionalliga teams, stay as the template had them.)

Slots per league (the numbers of UEFA places, promoted and relegated clubs come from the original data of that league): top division
champion, runner-up, UEFA places, cup winner, cup finalist, promoted; lower divisions relegated and promoted. Defaults, since FM has
no trophy data: the weakest clubs by reputation are promoted, in lower divisions the strongest are relegated, in the top division the
best last league position is champion and cup winner, the second best runner-up and finalist, then the UEFA places. The Teams tab
has a "Season results..." button: every slot can be given to another club of the league (the two clubs swap slots); choices are stored
per league by FM club id. Non-league clubs: per nation the champion (best last position) and the cup winner (second best), as positions
among the nation's clubs in the file; the window lists every nation of the triplets.
