# Patches and the single-file release

## One executable
`tools\publish.ps1` builds `dist\A2G-SAV-Converter.exe`. It is the whole program in one file: the converter, the libraries
(`A2G.Convert`, `A2G.Sav`), the Football Manager reader `fmsave.exe` and the patch builder / patcher. The libraries and fmsave are
embedded resources (see `src\A2G.SavConverter\Services\EmbeddedAssets.cs` and the `EmbedLibraries` target in the project file):
the libraries are loaded from the resources, fmsave.exe is unpacked once to `%LOCALAPPDATA%\A2G-SAV-Converter\fmsave\<size>\`.
`tools\fmsave\fmsave.exe` must exist before publishing (build it once with `tools\build-fmsave.ps1`). The script copies the finished
exe alone into an empty folder and starts it as a smoke test.

## Making a patch
Tab "Write & Install" -> **Generate patch file...** (right next to **Install**, because it takes the same data set: the generated files of the
project, not the live game data, under the name in the box). The file is named `A2G-Patch_<name>.exe` (for the name FM26: `A2G-Patch_FM26.exe`); the description dialog shows this name and the save dialog proposes it. The converter asks for an optional description and for
the file to save, then writes a copy of itself with the project's data set attached. "Only these parts" (the partial install choice)
applies: a partial patch only replaces the chosen files and leaves the rest of the user's data alone. The parts are defined once in
`A2G.Convert/Writing/PartialInstall.cs` (`InstallParts`): name pools, Avereine, the start year (`KLEINIG.SAV`) and the countries; a country
brings its team, stadium, league and staff files and its crests (`WAPPEN\VW_<letter><n>.BMP` and `<letter>0_00.BMP`). A data set
installed from a patch shows `Patch: <name>` as its source in the list.

File layout of a patch: `[the program][ZIP: data/DATA.DEF/..., data/DATA.VER/..., patch.json][8 bytes payload length][ASCII "A2GPATCH"]`
(`src\A2G.Convert\Patching\PatchPackage.cs`). `patch.json` has the name, description, date, the partial flag and size and SHA-256 of every
file; unpacking checks them. A patch built from a patch program (or the converter) never carries two payloads.

## The patcher
When the program finds a patch at the end of its own file it opens the patcher window instead of the converter:
- the Anstoss 2 folder is filled in like in the converter (the folder used last, else the same detection); Browse changes it;
- **Install this patch** installs the data as a new profile and makes it live, through the same profile manager and marker file as the
  converter (`docs\Dev\profiles.md`): the Original data is stored on first use, edited live files can be saved into the active profile first,
  the folders are swapped by rename after a verified copy, and the game must be closed;
- the list shows all data sets of the installation; select the Original one (or any other) and **Switch to the selected data set** to
  go back. Patches and converter installs live in the same list.

## Command line (for installers)
A patch exe understands:

| Option | Meaning |
|---|---|
| `-q` | Quiet mode: install and activate the patch with no window, no popup and no question. Also `/q`, `--quiet`. |
| `-F <folder>` | The Anstoss 2 folder (where anstoss2.exe, DATA.DEF and DATA.VER are). Also `-F:folder`, `-F=folder`, `/F folder`. Without `-q` it only fills in the window. |

Example: `"My Patch.exe" -q -F "C:\Games\ANSTOSS 2"` (with `start /wait` or `Start-Process -Wait` to get the exit code).

Quiet mode never shows a window or message box. It installs through the same profile logic as the window: the patch becomes a data set named
after it and is made live, the previous data stays as its own data set. Edited live files are saved into the active data set first (nothing is
lost), and a patch that is already installed is only activated again, so running it twice changes nothing. Without `-F` the folder used last
(or the automatic detection) is taken. Problems are reported by the exit code and one line of text on the console / redirected output:

| Exit code | Meaning |
|---|---|
| 0 | installed and live (or already installed and now live) |
| 1 | no Anstoss 2 folder (no `-F`, nothing remembered or found) |
| 2 | the folder is not an Anstoss 2 folder (DATA.DEF or DATA.VER missing) |
| 3 | the patch is damaged (its data cannot be read or a checksum does not match) |
| 4 | anything else, for example Anstoss 2 or its editor is running |

Without `-q`, a patcher that finds no Anstoss 2 folder automatically says so in a popup and asks the user to select it.

## Same name: replacing a data set
Installing under a name that already exists replaces that data set, in the converter's Install button and in the patcher alike. The
window asks first ("Replace it with the new data?"); the new data is built and verified next to the old one before it takes its place,
so a failure leaves the old data set untouched. The Original data set can never be replaced. The suggested name is now the plain patch
or project name, so installing an updated version of the same patch simply replaces the older one. Quiet mode (-q) replaces a data set of
the same name without asking (when the content is identical it is only activated).
