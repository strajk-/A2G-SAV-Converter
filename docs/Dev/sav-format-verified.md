# Anstoss 2 .sav formats - verified layouts

Verified by parsing and re-serialising every file of the original install (`tests/A2G.Sav.RoundTrip`,
171 files in DATA.DEF + DATA.VER, all byte-identical).

General: text files, one field per line, every line ends with CRLF, Latin-1 (0xFC = u-umlaut).
DATA.DEF and DATA.VER are identical in the original install, except DATA.VER also has `Kunden.sav`.
Integers are canonical decimal text (no leading zeros / plus signs). Magic number `842610482` is the first line of
Avereine, Land*, LAENDER, JUGE*, JUGEND*, KLEINIG and Fangesa.

## Land*.sav (LandDeut, LANDENGL, LANDFRAN, LANDITAL, LANDSPAN)
```
header (6 lines): magic, number of LEAGUES, number of TEAMS, 3, 3, 1 (last = 2 in Italy; meaning of the three unknown)
team x TEAMS:
  4 text lines: name, short name ("---" = none), city, nickname
  trainer block:  2 name lines + 3 ints
  staff block:    2 name lines + 21 ints: [2-7] kits (see "Kits"), [17] results of the last season (see conversion-rules.md),
                  [20] number of players that follow; the others are not decoded
  players (variable count, 15 lines each):
     last name, first name, then 13 ints:
     Unknown0 (always 0 in practice), Skin 0-2, Hair 0-5, Age, Strength 1-7, Nationality (LAENDER index),
     Position1, Position2, Position3 (1-10, 0 = none), PositiveSkills, NegativeSkills, Traits, CharacterHealth (bitmasks)
tail: 40 spare club names (1 per line), then 1 person block (2 names + 3 ints)
```
The first header number is the league count (Germany 6, England 3, France 2, Italy 1, Spain 1), NOT a country id.
`Stad*.sav` has one 31-line record per team of the matching Land file, in the same order.
The team->league assignment is made by the matching `LIGA*.SAV` team ids.

## Leagues in the original data
| Land file | leagues | teams in file | teams in LIGA files |
|-----------|---------|---------------|---------------------|
| LandDeut | 6 | 124 | LIGA1DEU 18, LIGA2DEU 18, LIGARLNO/OS/SU/SW 18 each = 108 |
| LANDENGL | 3 | 75 | LIGA1ENG 20, LIGA2ENG 24, LIGA3ENG 24 = 68 |
| LANDFRAN | 2 | 46 | LIGA1FRA 18, LIGA2FRA 22 = 40 |
| LANDITAL | 1 | 24 | LIGA1ITA 18 |
| LANDSPAN | 1 | 26 | LIGA1SPA 20 |

The teams beyond the league slots (Germany 16, England 7, France 6, Italy 6, Spain 6) are extra clubs
that are not in any simulated league.

## LIGA*.SAV
`n`, then `n` team ids, then the fixture table: `2*(n-1)` matchdays x `n` entries (all 13 files have this shape).
Team ids are **1-based positions in the matching Land file's team list** (ids run 1..team count, never 0).
Fixture entries are 1-based positions within the league's own `n` slots (1..n), i.e. "the n-th team of this league".
A Land team that no LIGA file references is a *spare* club (not in any simulated league): Germany 16, England 7,
France 6, Italy 6, Spain 6. In Italy and Spain they are real second-division clubs (e.g. FC Neapel, RC Sevilla),
so they are probably the promotion/relegation pool; how the engine builds a second tier for Italy/Spain when that
country is the main league is NOT visible in the files.

## Avereine.sav (non-league clubs)
```
magic
130 x club (no count line):
  4 text lines: name, short name, stadium, city
  11 ints: home shirt/shorts/socks, away shirt/shorts/socks, country (LAENDER index), stadium capacity,
           financial power, article type, flag
  11 players (15 lines each, same record as Land players)
tail: 40 x (spare club name, country index), then 30 x (country, champion, cup winner)
```
The 30 triplets are the 30 European nations; champion and cup winner are positions (from 0) among the nation's clubs in the file, i.e. the
last season's results of that nation. The first club of a nation is position 0.

## LAENDER.SAV
magic, then 76 nations x 9 lines: name, rating, code, 6 ints. The index in the file is the country id
(0 = "Sonstige" = others, 6 = Belgien, 14 = Deutschland, 16 = England, 20 = Frankreich, 28 = Italien, 63 = Spanien).
There is no Yugoslavia, Serbia, Bosnia, ...; use Sonstige. Zaire is still listed.

## Fixed-record files (no magic unless noted)
| Files | Record |
|-------|--------|
| TRAI*.SAV | 5 lines: first name, last name, 3 ints (40 DEU/ENG/FRA, 20 ITA/SPA) |
| TTRA*.SAV | 4 lines: 2 names, 2 ints (20) |
| MANA*.SAV | 4 lines: 2 names, 2 ints (40 / 20) |
| NSCH*.SAV | 5 lines: 2 names, 3 ints (40) |
| SPON*.SAV | 19 lines: name, 15 ints, font name, 2 ints (40) |
| STAD*.SAV | 31 lines: name, city, 29 ints |
| PROM*.SAV | 3 lines: 2 names, 1 int (20) |
| ISCHIRIS.SAV | 5 lines: 2 names, 3 ints (40) |
| Kunden.sav (VER only) | 3 lines: 2 names, 1 int (184) |
| JUGE*.SAV, JUGEND*.SAV | magic, then (first name, last name) pairs |
| KLEINIG, Fangesa, FanWaves | kept as raw lines |
| ExSpiel.sav | empty in the original install |

## Limits stated by the original editor (EDITOR.HLP, plain text inside the file)
- Player, prominent person and referee names: last name 1-16 characters, first name 1-9 characters
  (in the game a 16-character surname is shown garbled, so the converter uses 15). Stadium name 3-28 characters.
- Club text fields (maximum seen in the original files): team name 19, city 16, nickname 18, stadium 26 (editor: 28),
  spare club names 19; Land short name is a 3-letter code or "---" (no short name); Avereine short name 12.
- Player strength 1-7. Age 17-50. A field player may have at most two of the positive abilities.
  At most two secondary positions per player (Position2/Position3).
- 40 referees per country, 40 trainer names per country (Italy and Spain 20).
- Clubs can be moved between leagues in the editor ("Restliche Vereine" = clubs in no league = the spare clubs),
  but only while the target league has a free slot: league sizes are fixed.
- The editor reports the teams with the most/fewest players and the average, but states no squad-size limit.
  Observed in the original data: 16-30 players per team (many teams at exactly 30), 1-5 goalkeepers (usually 2-3).
  Avereine clubs always have exactly 11 players.

## Player abilities and character (names from EDITOR.HLP; the ability bit order is an inference, traits and characters are verified)
- Field player abilities, at most two per player (11 -> fits the 11 bits 2..2048 of PositiveSkills):
  Kopfball, Zweikampf, Schnelligkeit, Schusskraft, Freistoss, Flanken, Torinstinkt, Laufstaerke, Technik,
  Spielmacher, Beidfuessigkeit.
- Main character trait (one): Hitzkopf, Frohnatur, Mann ohne Nerven, Nervenbuendel, Phlegma, Geldgeier, Musterprofi,
  Skandalnudel.
- One additional trait: Fuehrungsperson, Kaempfernatur, Trainingsweltmeister, trainingsfauler Spieler, Treter,
  fairer Spieler, Schwalbenkoenig (list may continue), plus a health state (e.g. Knieprobleme).
- Goalkeepers have their own set (e.g. starke Reflexe); their bit table is not verified.
- Working assumption for PositiveSkills / NegativeSkills (column 9 and 10 of a player): bit value = 2 << index in the
  editor's order (Kopfball 2, Zweikampf 4, Schnelligkeit 8, Schusskraft 16, Freistoss 32, Flanken 64, Torinstinkt 128,
  Laufstaerke 256, Technik 512, Spielmacher 1024, Beidfuessigkeit 2048). Evidence: bit 1 is never used, and in the original data Torinstinkt is 2.4x as
  common among strikers, Spielmacher 4.1x among attacking midfielders, Technik 2.4x among position 9, Kopfball and
  Zweikampf ~2x among defenders. Goalkeepers (position 1) reuse the bits with other meanings.
- Character / trait columns (11 and 12): verified from the game's code (it loops `mask & (1 << idx)` over string-id tables read from
  anstoss2.exe at 0x6c7718 / 0x6c7748; the names come from RESDLL.dll, ids 1153-1171).
  - Traits, column 11 (+0x2c), bit value = 1 << index, stored as is:
    1 Fuehrungsperson, 2 Kaempfernatur, 4 Trainingsweltmeister, 8 trainingsfaul, 16 Treter, 32 fairer Spieler,
    64 Schwalbenkoenig, 128 Allrounder, 256 Flexibler Spieler, 512 Heimspieler, 1024 Auswaertsspieler,
    2048 Talent ("Talent" under 25, "ewiges Talent" from 25; 362 of 397 non-goalkeepers with the bit are under 25).
    The game shows only the first five traits for goalkeepers (the shipped data still has 64/128/2048 on some).
  - Character, column 12 (+0x2e): the game tests memory bit idx 0..7 = Hitzkopf, Frohnatur, "Mann fuer Grosses" (the
    description sentence says "Mann ohne Nerven"), Nervenbuendel, Phlegma, Geldgeier, Musterprofi, Skandalnudel.
    In the files the value is shifted left by one (2 Hitzkopf, 4 Frohnatur, 8, 16, 32, 64 Geldgeier, 128 Musterprofi,
    256 Skandalnudel). The shift is inferred (8 character values 2..256 in the data, knee problems = 2048 = memory bit
    0x400 which the knee-injury event checks, Hitzkopf = 2), not read from the loader.
    Health states in the same column: 512 "kann einiges wegstecken", 1024 verletzungsanfaellig, 2048 Knieprobleme
    (names from the order of the description sentences; 2048 confirmed by the event code).
  - Not mapped by the converter yet: FM has no direct equivalent; rules still have to be defined.
- Positions (from EDITOR.HLP, ten main positions, matches the data): 1 Torwart, 2 Libero, 3 Manndecker,
  4 linker Verteidiger, 5 rechter Verteidiger, 6 defensiver Mittelfeldspieler, 7 linker Mittelfeldspieler,
  8 rechter Mittelfeldspieler, 9 offensiver Mittelfeldspieler, 10 Stuermer.
  Each player has one main position and up to two secondary positions (Position2/Position3, 0 = none).
- Squad size (EDITOR.HLP): every team needs at least 16 players at game start (the number may drop during play),
  at most 30; at least two goalkeepers are sensible.

## Open questions
- Meaning of the Land header numbers 3, 3, 1/2, of the undecoded numbers of the 21-int staff block, and of the tail person block.
- Which bits of the season-result number mean "runner-up" (6) and "relegated" (3) is inferred, not confirmed in the editor; bit 16 is unknown.
- Which of the two name lines of the trainer/staff blocks is the surname (Trai*.sav is first, last; Land player records are last, first).
- Whether the game reads DATA.DEF or DATA.VER (the original files are identical, so the converter writes both).

## Flags
The game does not build the flag file name from the nation code of LAENDER.SAV (Germany is "D" but its flag is GER.BMP). It
uses a list of 76 file names in nation order that is stored in anstoss2.exe just before the string "%s/klein/Flag%02d.bmp":
Wel (index 0 = Sonstige: the white world flag with the ball), Egy, Alb, Alg, Arg, Aus, Bel, Bol, Bra, Bul, Chl, Chi, Cos, Dan,
Ger, Ecu, Eng, Est, Far, Fin, Fra, Geo, Gha, Gri, Ni (Holland), Irl, Isl, Isr, Ita, Jam, Jpn, Kam, Kan, Kol, Kro, Let, Lib, Lit,
Lux, Mal, Mar, Mex, Mol, Nz, Nig, Nir (Nordirland), Nor, A, Par, Per, Pol, Por, Pue, Rum, Rus, San, Sau, Sco, Swe, Ch, Sen,
Swk (Slowakei), Slo (Slowenien), Spa, Sko, Tsc, Tun, Tur, Ukr, Hun, Uru, Usa, Ven, Wal, Zai, Zyp. The converter uses the same
list (PlayerInspector.FlagFiles). FLAGGEN\KLEIN\FlagNN.bmp are the small versions (not needed).

## Crests (WAPPEN)
Not stored in any .sav. The game loads `WAPPEN\VW_<D|E|F|I|S><n>.BMP` (n = 1-based position of the team in that country's Land
file; Germany 124, England 75, France 46, Italy 24, Spain 26 files) and uses `D0_00 / E0_00 / F0_00 / I0_00 / S0_00` for a team index
beyond the country's team count (read from the game's code, not tested in the game). Files are 32x32, 8-bit BMP,
2102 bytes, near-white background (251,250,249). Avereine clubs have no crest file.

## Player count of a team (found when the first generated data set crashed the game)
The last of the 21 numbers of the second staff block (`LandTeam.StaffBlock.Values[20]`) is the number of player records that follow
the team (the game's Land loader reads exactly that many; 124/124 teams of the original LandDeut match).
The writer must set it to the real player count; copying it from the template team makes the game read the wrong number of
records. `SavValidator` checks this.

## Avereine.sav nation layout (second cause of the game aborting on "new game")
The game groups the clubs of Avereine.sav by nation while loading (its loader keeps the
first club index and a running index per nation) and needs the file sorted by nation. The shipped file has exactly: 130 clubs of
30 European nations (Belgien 6, Bulgarien 4, Daenemark 6, Finnland 4, Griechenland 6, Holland 6, Irland 3, Island 4, Israel 4,
Kroatien 4, Luxemburg 3, Malta 3, Nordirland 3, Norwegen 4, Oesterreich 6, Polen 4, Portugal 6, Rumaenien 6, Russland 6,
Schottland 6, Schweden 4, Schweiz 4, Slowakei 3, Slowenien 3, Tschechien 3, Tuerkei 6, Ukraine 3, Ungarn 4, Wales 3, Zypern 3)
and 40 spare club names of 32 overseas nations, also sorted. The 30 country triplets at the end are those 30 nations. Clubs of other
nations or with nation 0 ("Sonstige"), or an unsorted file, aborted the game after entering the player data.
The converter fills the layout nation by nation (`AvereineSlots`); slots of nations without enough FM clubs keep the shipped club.
`SavValidator` compares the nation sequence with the shipped file.

## Start year (KLEINIG.SAV)
The game reads its "current season year" from KLEINIG.SAV: the line `1998` followed by `16` and `51` (line 135 of 290; 51 = the number of
(year, flag, value) triples for the even years 1998-2098 that follow). Day and month are fixed in the game (6 July). The converter can write another
year there (`GameYear`, setting `StartYear`, default: the year of the FM save); the game knows 1998-2098.

## Kits
Land team, second staff block (`StaffBlock.Values`): [2] home shirt, [3] home shorts, [4] home socks, [5] away shirt, [6] away shorts, [7] away socks.
A shirt value is `pattern * 16 + colour`: pattern 0 neutral, 1 laengs, 2 quer, 3 Halb/Halb, 4 Schulter, 5 Brustring, 6 Aermel w. H. (the editor's
"Streifenmuster"); the second colour of a striped shirt is the colour of the shorts (EDITOR.HLP). Shorts, socks and the colour part of a shirt are one of 13 colours (0-12).
Checked: slot "Bertha BSE Berlin" = [32,3,3,0,0,0] = home quer/white with blue shorts and socks, away white, as Editor.exe shows. Colours confirmed by club: 0 white, 2 yellow,
3 blue, 4 green, 11 black; the whole palette is listed below. Avereine clubs have the six values directly (HomeShirt ... AwaySocks).
The converter writes the kit of every club (a default from the club id, or the user's choice, see conversion-rules.md).

### Kit colour palette (read from Editor.exe by clicking shirt, shorts and socks)
13 colours, in the order the editor cycles them (index = the colour part of a kit value): 0 white, 1 red, 2 yellow, 3 blue, 4 light green, 5 dark green,
6 teal, 7 light blue, 8 brown, 9 purple, 10 orange, 11 dark grey (black), 12 maroon. Sampled RGB of the editor's shirt (shaded photo): red 183,6,29; yellow 240,234,25;
blue 0,3,146; light green 75,191,21; dark green 0,112,0; teal 0,146,133; light blue 143,222,239; brown 138,60,24; purple 160,18,156; orange 252,123,13;
dark grey 67,67,67; maroon 142,22,77. The editor skips the colour of the other set's shirt for the home shirt (the shirt colours of both sets must differ), so a
shirt cycle looks like 12 colours. Stripe patterns: 0 neutral, 1 laengs, 2 quer, 3 Halb/Halb, 4 Schulter, 5 Brustring, 6 Aermel w. H.
