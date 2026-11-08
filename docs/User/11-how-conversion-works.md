# 11. How the conversion works

This page explains the rules behind the result, so you know what to expect and what you can change. Most rules are adjustable in
[Options](03-options.md) or by hand in [Teams](05-teams.md) and [Players](06-players.md).

## Principles

- The output consists of Anstoss 2 **data files** (`.sav`) and **crest images** only. The game program is never changed and no nation
  of the game is renamed, added or removed.
- Every generated file keeps the **exact number of records** of the original (teams, nations, stadiums, name pool entries ...), because
  the game expects them. A league has a fixed size.
- Where FM provides the data, **no original Anstoss 2 data is left**: clubs, players, staff and names all come from FM. Where FM has
  no data (for example sponsors) the original stays.
- Football Manager 26 and 27 data is read through a built-in reader; the save is never modified.

## The structure of Anstoss 2

Five countries have playable leagues; their sizes are fixed:

| Country | Leagues |
|---------|---------|
| Germany | 1. Liga (18), 2. Liga (18), four Regionalligen of 18 (Nord, Ost, Sued, Suedwest) |
| England | tier 1 (20), tier 2 (24), tier 3 (24) |
| France | tier 1 (18), tier 2 (22) |
| Italy | one league (18) |
| Spain | one league (20) |

Each country file also holds a few **spare teams**: clubs that play in no league (Germany 16, England 7, France 6, Italy 6,
Spain 6). In addition there are **130 non-league clubs (Avereine)** of 30 European nations and 40 more club names of overseas nations.
All of this is loaded when the game starts, regardless of which league you pick as your own later.

## Which FM clubs go where

1. Only **men's first teams** of clubs count (no women's teams, no reserve or youth sides).
2. The program works out the **leagues of every nation from the fixtures** of the save (which clubs play each other).
3. For each country the FM divisions are laid over the Anstoss 2 leagues from the top down. When an FM league has **more** clubs than
   the Anstoss 2 league (for example 20 instead of 18), the clubs with the **lowest reputation** move one division down. When it has
   **fewer**, the **highest-reputation** clubs of the division below move up to fill the gap. What is left over after the last
   division fills the spare teams and the spare club names.
4. A club needs **enough real players** in the FM save (default 11) to get a slot; an FM save only loads some leagues completely.
5. **German Regionalligen:** the FM save has no club locations, so the 72 clubs of the third tier are chosen by reputation and dealt
   into the four groups in a snake order (1 to Nord, 2 to Ost, 3 to Sued, 4 to Suedwest, 5 to Suedwest, 6 to Sued ...). The four groups
   are equally strong; the result is always the same for the same data.
6. Inside a league, **bigger stadium slots go to higher-reputation clubs**: the original stadium shapes are kept and paired with the
   clubs by size.

You can change any of this by hand with [swaps in the Teams tab](05-teams.md).

## Non-league clubs (Avereine)

The 130 clubs come from 30 European nations with a fixed number of clubs each (for example Belgium 6, Bulgaria 4, Denmark 6). The game
needs the clubs **grouped by nation in exactly this layout**, so the converter fills it nation by nation with the best FM clubs of each
nation. A nation without enough FM clubs keeps some original clubs (a warning says so). Every non-league club has exactly 11 players.
A club with too few players in the FM save is topped up from unused players, same nation first, closest to the club's average
ability; the **Plan** summary and the log list those clubs.

## Squads

Squads are picked **by position**, not just by ability ([Options](03-options.md)): for every Anstoss 2 position the best players by
the ranking below are taken.

- The ranking is **current ability plus a share of the growth room** for young players (default: a 100/170 talent ranks like a 135
  player). Players under 17 are never picked.
- A missing position is filled from the club's own players: first those who list the position as a secondary position, then the
  substitution chains (no striker: offensive, right, left midfielder; no libero: offensive midfielder; no left back: left midfielder,
  right back, central defender), then anybody else.
- A club that still has too few players or goalkeepers is topped up with free players of its nation. This is a workaround for the
  incomplete FM save: Anstoss 2 has no free agents, the players simply join the team.
- Players can be added and removed by hand in the team information.

## How a player is converted

| Anstoss 2 value | How it is made |
|-----------------|----------------|
| **Name** | FM name fitted to the game's limits: last name 1-15 characters, first name 1-9 characters. |
| **Age** | The FM age, limited to 17-50. |
| **Nationality** | FM nation to Anstoss 2 nation by name. Nations Anstoss 2 does not have (Serbia, Montenegro, Bosnia ...) become **Sonstige** ("others"). |
| **Strength (1-7)** | From the FM current ability with the strength limits in Options. Never 8: that is a random event of the game. |
| **Positions** | The ten Anstoss 2 positions are derived from FM's position ratings (for example left back = best of left back and left wing-back). The best rating is the main position; up to two more at the secondary rating limit (default 15). Left/right assignment of wide players is not verifiable from the data. |
| **Abilities** | Rules that combine FM attributes (for example Schnelligkeit from pace and acceleration), at most two positive and one negative by default. Goalkeepers have their own abilities. |
| **Trait, character, health** | Rules on FM attributes and personality (for example Hitzkopf from a very low temperament). One character or health state and up to one trait per player by default; the **Talent** flag comes on top for young players with a lot of growth room. |
| **Skin and hair** | A rough default from nationality, skin and age; you can change both per player. FM has no appearance data. |

Market value, salary, form and statistics are calculated by the game when a new game starts.

## Staff, referees and name pools

- Each team's **trainer** is the best-rated FM staff member of the club. FM saves do not label jobs, so this is a rule: it can be a
  director or a coach instead of the manager.
- The **trainer, manager, referee and prominent-person files** of each country are filled with the best-rated unused staff of the
  country's nation (trainers and managers by their ratings, referees from their competence and hardness, prominent persons with a
  favourite club). A nation with too few staff members keeps some original records and a warning appears.
- The **name pools** used for new players during the game are filled with the names of FM players of the nations each pool serves,
  topped up with staff names.

## Club information, kits and crests

FM saves have none of these, so they are generated, and you can change every one ([Teams](05-teams.md)):

- **City** is the place word of the club name, **nickname** the city, **stadium name** "<city> Stadion".
- **Stadium capacity** is derived from the club's reputation (about 3,000 for small clubs up to about 65,000 for the biggest);
  editable from 3,000 to 110,000. The stadium's sections are rescaled to it.
- **Kits** are made-up pairs of colours per club; home and away shirts always differ.
- **Crests** are generated per club (round badge in the home kit colours with letters), or your own image.

## Results of the last season

Last season's champion, runner-up, UEFA Cup places, cup winner and finalist, promoted and relegated teams are written as markers on
the teams. Without trophy data in FM the converter uses these rules (changeable in **Season results...**): the best last league
position is champion and cup winner, the second best runner-up and cup finalist, the next ones get UEFA Cup places; the lowest
reputation are promoted; in lower divisions the highest reputation are relegated. For non-league clubs one champion and one cup winner
per nation are written.

## Start year

The game's start year is written into `KLEINIG.SAV`: by default the year of the FM save, or the year you choose.

## What stays original

Sponsors and a few other files for which FM has no data, the unknown numbers of some game structures, and everything the converter does
not touch. The [Plan](04-plan.md) summary and the log tell you about places where original data was kept.

Next: [Files and folders](12-files-and-folders.md).
