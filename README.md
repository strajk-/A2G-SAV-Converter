# A2G-SAV-Converter

WPF application (.NET Framework 4.8) that converts Football Manager 26 data into Anstoss 2 `.sav` data sets.

## Layout

| Path | Purpose |
|------|---------|
| `A2G-SAV-Converter.sln` | Visual Studio solution |
| `src/A2G.SavConverter/` | The WPF app. `Services/FmSaveExporter.cs` is the only code that knows about FM. |
| `src/A2G.Sav/` | Library: typed readers/writers for the Anstoss 2 `.sav` files (byte-exact round trip) |
| `src/A2G.Convert/` | Engine: loads the FM CSV exports, detects leagues from fixtures, builds the editable `ConversionPlan` (reputation cascade, squads, Avereine clubs) and maps players using `ConversionSettings` |
| `tests/A2G.Convert.Cli/` | Console: builds a plan from an `fm_export` folder and prints a report (`A2G.Convert.Cli.exe <fm_export> <Anstoss 2 folder>`; the folder can also come from the environment variable `A2G_ANSTOSS2`), and the self tests used by `run-checks.ps1` |
| `tests/A2G.Sav.RoundTrip/` | Console test: parses every file of an install and requires identical bytes (`A2G.Sav.RoundTrip.exe <install path>` or `A2G_ANSTOSS2`) |
| `docs/User/` | The user guide (Markdown, written for publishing online): start with `docs/User/README.md` |
| `docs/Dev/` | Developer documentation: file formats (`sav-format-verified.md`), conversion rules, profiles, projects, patches |
| `third_party/fmsave/` | Vendored [fmsave](https://github.com/rhiever/fmsave) 0.10.0 source (MIT, (c) Randal S. Olson) |
| `tools/build-fmsave.ps1` | Builds `tools/fmsave/fmsave.exe` from the vendored source (private venv in `tools/_build`) |
| `tools/fmsave/` | The built `fmsave.exe` (not committed); the build embeds it into the app's exe, which unpacks it on first use |
| `tools/publish.ps1` | Builds the single-file release `dist\A2G-SAV-Converter.exe` (see `docs/Dev/patches.md`) |

## Build

1. `powershell -ExecutionPolicy Bypass -File tools\build-fmsave.ps1` (needs Python 3.12+; only for this step; `tools\publish.ps1` runs it when fmsave.exe is missing or outdated)
2. Open `A2G-SAV-Converter.sln` in Visual Studio, or `dotnet build`.

## The app

`A2G-SAV-Converter.exe [project folder] [--autoplan]` (`--autoplan` loads the project's export and builds the plan on start; without a
folder the last open project is opened). The tabs are numbered steps of one linear workflow: **1 Project & Export** (the project list, the FM
save, picked with Browse or dropped onto the window, with a line showing its game version, build and compatibility; the Anstoss 2 folder is
found automatically where possible; the game does not have to be installed), **2 Options** (squad rules, strength bands, ability rules; saved in
the project), **3 Plan** (build the plan; needs the export), **4 Teams** (swap teams between leagues or with unused FM clubs, edit squads,
club information, kits and the results of the last season; edits are saved and re-applied after a rebuild), **5 Players** (FM profile -> arrow ->
Anstoss 2 "Spielerinfo" preview using the current settings; skin and hair), **6 Crests** (own and painted crests), **7 Write & Install** (write
the `.sav` files including all edits and validate them, then install them as a profile, switch/rename/delete profiles, partial installs, and
generate a patch file). Steps 4 to 6 stay greyed out until a plan exists, and step 3 until an export exists.

Automated checks (nothing touches the real install): `powershell -File tests\run-checks.ps1 -Anstoss2 "<A2 folder>" -FmExport <fm_export folder>`.
Images (ability icons, flags) were extracted once with `tools/extract-assets.ps1`; the game's DLL is not used at runtime.

## Project folder

Everything the tool produces lives in one project folder. Projects are named: each is a folder below `%APPDATA%\A2G-SAV-Converter\Projects` (see docs\Dev\projects.md); the project list is at the top of the first tab, and any other folder can be opened too:

```
<Project>\
  fm_export\   CSVs exported from the FM save
  mapping\     settings and manual edits
  Crests\      own crest images (named by FM club id)
  Anstoss2\    the deliverable; copy its contents over the Anstoss 2 install folder
    DATA.DEF\*.sav, WAPPEN\*.BMP
    DATA.VER\*.sav, WAPPEN\*.BMP
```

Only `.sav` files and the crest `.bmp` files in `WAPPEN` are written; no binaries are edited and no nation records are renamed.

## Export from an FM save

The app runs `fmsave export <save> <table> --all --format csv` for `players`, `clubs`, `nations`, `fixtures` and `staff`, and adds the nation
names (from the installed FM database, or from a bundled table when FM is not installed), into the project's `fm_export` folder.

Supported FM26 builds (fmsave 0.10.0): 26.1.0, 26.3.1, 26.3.2.

Notes:
- Use a **fresh** save (start of a new career, all wanted leagues loaded). A save from a long career holds generated future players, not real ones.
- `nations.csv` only maps save nation ids to database ids; the names are in `nation_names.csv`.
- The FM-reading part is isolated so it can later be replaced by a native C# reader.

## Look and language

The app follows the Windows light/dark setting and display language by default (German or English; any other language
falls back to English). The header has a **Language** and a **Theme** drop-down to override both (remembered per user in
`%APPDATA%\A2G-SAV-Converter`). Texts live in `src/A2G.SavConverter/Localization/<code>.txt` (tab separated `key TAB text`,
key `0` = the language's own name); adding a file adds a language to the drop-down. In XAML use
`{loc:LocalizedText 10001}`, in code `Loc.T(10001, args)`.

## Patches and release
One single exe (tools\publish.ps1) and patch files for passing a data set on: see docs\Dev\patches.md.

## License

The source code of A2G-SAV-Converter is licensed under the **GNU General Public License, version 3** (GPL-3.0): see [LICENSE](LICENSE).
The vendored [fmsave](https://github.com/rhiever/fmsave) (MIT, (c) Randal S. Olson) keeps its own license in `third_party/fmsave`.
This is an unofficial tool, not affiliated with Ascaron, Sports Interactive or SEGA; game names and data belong to their owners.
