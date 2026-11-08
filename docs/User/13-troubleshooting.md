# 13. Troubleshooting and FAQ

## Frequently asked questions

**Does the converter change my Anstoss 2 installation or my FM save?**
The FM save is only read. Anstoss 2 is only changed when you press **Install** (or run a patch), and then the original data is kept as
the data set **Original**, so you can always go back ([Data sets and rollback](09-profiles.md)). The game program is never changed.

**Do I need Football Manager installed?**
No. You only need a save file. If the same FM version is installed, nation names are read from it; otherwise a built-in list is used.

**Which Football Manager versions work?**
FM26 (builds 26.1.0, 26.3.1, 26.3.2) and FM27. Older versions are not supported; the line under the save field tells you.

**Which leagues can be converted?**
Anstoss 2 has playable leagues for **Germany, England, France, Italy and Spain** with fixed sizes, plus 130 non-league clubs of 30
European nations. Clubs of other nations can only appear as non-league clubs or in the name pools; see [How the conversion
works](11-how-conversion-works.md).

**Why are some club names shorter or different?**
The game limits club names to 19 characters (city 16, nickname 18, stadium 26); names are shortened to fit.

**Why do players have strength 1 to 7 only?**
That is the game's range. Strength 8 is a random event inside the game and is never written. The limits that turn FM's current ability
into 1-7 can be changed in [Options](03-options.md).

**Can I use the program without Anstoss 2 installed?**
You need the Anstoss 2 data files as templates. A copy of the game folder is enough for converting; installing needs the real folder.

**Does the program need internet?**
No. It works offline and sends nothing anywhere.

**Is it available for macOS or Linux?**
It is a Windows program.

## Messages and problems

### Export

| Problem | Cause and solution |
|---------|--------------------|
| `Not a usable Football Manager save` | The file is not an FM save or is damaged. Choose a `.fm` file that FM wrote. |
| `Football Manager NN saves are not supported` | The save is older than FM26. |
| `not tested with this converter` | A newer FM version; try it, and check the result in the Players tab. |
| `fmsave.exe is missing` | The program file is incomplete. Download the single `.exe` again. |
| Few clubs or players appear later | The save only holds the leagues that were loaded when you started the career. Start a new career with all wanted leagues loaded. |
| `Nation names: ... only the built-in FM26 list is available, so a few nation names may differ` | An FM27 save without FM27 installed. A few nations may be named differently; check the nations of your clubs. |

### Plan and Teams

| Problem | Cause and solution |
|---------|--------------------|
| A league has fewer teams than expected (warning in the plan summary) | The FM save has too few clubs with a full squad for that division. Lower **Real FM players a club needs to qualify** in Options, or load more leagues in FM. |
| A slot "keeps the original team" | The FM export has no club for it. Swap a pool club in by hand. |
| A swap is refused | Teams can only be swapped inside one country; non-league clubs only with a club of the same nation. |
| `... already has the maximum of 30 players` | Select one of the squad players first; he is replaced. |
| `... would fall below the minimum of 16 players` | Removing or moving the player would leave the other team too small. |
| A team shows a warning sign | Open the team information; the **Problems** line says what is wrong (for example too few players or goalkeepers). |
| The Players tab shows nobody | Build the plan first, or tick **Include players not in the plan**. |

### Write

| Problem | Cause and solution |
|---------|--------------------|
| `Build the plan first` | Step 3 is not done. |
| `Validation problems (n)` | The lines name the file and the limit that was broken. Do not install; fix the cause (usually a manual edit or an unusual option) and write again. |
| Warnings after writing | Not errors: places where original data was kept or squads were topped up. Read them. |

### Install and patcher

| Problem | Cause and solution |
|---------|--------------------|
| `Close Anstoss 2 / the editor ... before changing profiles` | `anstoss2.exe` or `Editor.exe` is running. Close it. |
| `Select the Anstoss 2 folder` / not an Anstoss 2 folder | Choose the folder where `anstoss2.exe` is located; it must contain `DATA.DEF` and `DATA.VER`. |
| Access denied while installing | The game is in a protected folder such as `Program Files`. Start the converter or the patch **as administrator**. |
| `The live data files differ from profile ...` | The live files changed since the last install (for example with the game's editor). Choose to save them into that data set or discard them. |
| `A data set named ... exists already` | Choose **Yes** to replace it or cancel and use another name. |
| `The Original data set cannot be replaced` | Use another name. |
| `The patch is damaged and cannot be installed` | The patch file was changed or incompletely downloaded. Get it again. |
| Windows SmartScreen warns about a patch | Patches are unsigned programs. Only run patches from people you trust. |

### In the game

| Observation | Explanation |
|-------------|-------------|
| The game cannot load the data or stops while starting a new game | Switch back to **Original** ([Data sets and rollback](09-profiles.md)) so the game works again. Then write the files again and look at the validation result and warnings; if the problem stays, report it with the log. |
| A non-league (Avereine) player has a different strength than the preview | The game may change values of such players when it loads them (a random factor). |
| A club has no crest or the flag of its nation | Only the teams of the five main countries have crests; non-league clubs show the flag of their nation. |
| Crests or the start year did not change after a **partial** install | Crests come with their country (tick **Germany** for the German crests), the start year is the part **Start year**. Check that these parts were ticked. |
| A season-result marker looks different in the game's editor | The markers for runner-up and relegated teams were derived from the original data. Check them in the editor under the league composition (the legend shows the meaning of the colours) and adjust with **Season results...**. |

## Getting help

When you report a problem, please include the text of the log area at the bottom of the program (select it and copy) and say which
step you were in. Do not post your save file or anything you are not allowed to share.

Next: [Command line](14-command-line.md).
