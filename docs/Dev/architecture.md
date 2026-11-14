# Architecture and repository layout

Developer notes for working on the code. The user-facing guide is in [`docs/User`](../User/README.md).

## Layout

| Path | Purpose |
|------|---------|
| `A2G-SAV-Converter.sln` | Visual Studio solution |
| `src/A2G.SavConverter/` | The WPF app (.NET Framework 4.8). `Services/FmSaveExporter.cs` is the only code that knows about FM. |
| `src/A2G.Sav/` | Library: typed readers/writers for the Anstoss 2 `.sav` files (byte-exact round trip) |
| `src/A2G.Convert/` | Engine: loads the FM CSV exports, detects leagues from fixtures, builds the editable `ConversionPlan` (reputation cascade, squads, Avereine clubs), maps players using `ConversionSettings`, writes the files, manages profiles, projects and patches |
| `tests/A2G.Convert.Cli/` | Console: builds a plan from an `fm_export` folder and prints a report (`A2G.Convert.Cli.exe <fm_export> <Anstoss 2 folder>`; the folder can also come from the environment variable `A2G_ANSTOSS2`), and the self tests used by `run-checks.ps1` |
| `tests/A2G.Sav.RoundTrip/` | Console test: parses every file of an install and requires identical bytes (`A2G.Sav.RoundTrip.exe <install path>` or `A2G_ANSTOSS2`) |
| `tests/run-checks.ps1` | Builds the solution and runs every automated check on scratch copies (nothing touches a real install) |
| `docs/User/` | The user guide (Markdown, written for publishing online) |
| `docs/Dev/` | Developer documentation: this page, file formats (`sav-format-verified.md`), conversion rules, profiles, projects, patches, screenshots |
| `third_party/fmsave/` | Vendored [fmsave](https://github.com/rhiever/fmsave) 0.12.4 source (MIT, (c) Randal S. Olson) |
| `tools/build-fmsave.ps1` | Builds `tools/fmsave/fmsave.exe` from the vendored source (private venv in `tools/_build`) |
| `tools/fmsave/` | The built `fmsave.exe` (not committed); the build embeds it into the app's exe, which unpacks it on first use |
| `tools/publish.ps1` | Builds the single-file release `dist\A2G-SAV-Converter.exe` (see [patches.md](patches.md)) |
| `tools/extract-assets.ps1` | Extracts the ability icons and flags from an Anstoss 2 install into the app's `Assets` folder (run once; the game's DLL is not used at runtime) |

## Build

1. `powershell -ExecutionPolicy Bypass -File tools\build-fmsave.ps1` - needs Python 3.12+, only for this step;
   `tools\publish.ps1` runs it when `fmsave.exe` is missing or outdated.
2. Open `A2G-SAV-Converter.sln` in Visual Studio, or run `dotnet build`.
3. `powershell -File tools\publish.ps1` builds the single-file release into `dist\` and runs a self check on it.

## Automated checks

```
powershell -File tests\run-checks.ps1 -Anstoss2 "<Anstoss 2 folder>" -FmExport "<fm_export folder of a project>"
```

The checks cover the round trip of the original and the written files, conversion, writing and validation, the plan editor, the profile
manager, patch files and their installation, and the project list. Nothing is written to the real Anstoss 2 folder.

## The app

`A2G-SAV-Converter.exe [project folder] [--autoplan]` - `--autoplan` loads the project's export and builds the plan on start; without a
folder the last open project is opened. `--selfcheck` checks that the single exe is complete and exits; `--screenshots=<folder>` renders
the pictures of the user guide ([user-guide-screenshots.md](user-guide-screenshots.md)). With a patch attached to its end the same exe
opens as the patcher ([patches.md](patches.md)).

The tabs are numbered steps of one linear workflow: **1 Project & Export**, **2 Options**, **3 Plan**, **4 Teams**, **5 Players**,
**6 Crests**, **7 Write & Install**. Steps 4 to 6 stay greyed out until a plan exists, and step 3 until an export exists.

## Project folder

Everything the tool produces lives in one project folder. Projects are named: each is a folder below
`%APPDATA%\A2G-SAV-Converter\Projects` (see [projects.md](projects.md)).

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

The app runs `fmsave export <save> <table> --all --format csv` for `players`, `clubs`, `nations`, `fixtures` and `staff`, and adds the
nation names (from the installed FM database, or from a bundled table when FM is not installed), into the project's `fm_export` folder.

Supported FM26 builds (fmsave 0.12.4): 26.1.0, 26.3.1, 26.3.2; FM27 saves are read as well.

- Use a **fresh** save (start of a new career, all wanted leagues loaded). A save from a long career holds generated future players.
- `nations.csv` only maps save nation ids to database ids; the names are in `nation_names.csv`.
- The FM-reading part is isolated so it can later be replaced by a native C# reader.

## Look and language

The app follows the Windows light/dark setting and display language by default (German or English; any other language falls back to
English). The header has a **Language** and a **Theme** drop-down to override both (remembered per user in `%APPDATA%\A2G-SAV-Converter`).
Texts live in `src/A2G.SavConverter/Localization/<code>.txt` (tab separated `key TAB text`, key `0` = the language's own name); adding a
file adds a language to the drop-down. In XAML use `{loc:LocalizedText 10001}`, in code `Loc.T(10001, args)`.
