# 14. Command line

## The converter

```
A2G-SAV-Converter.exe [project folder] [--autoplan]
```

| Argument | Meaning |
|----------|---------|
| *project folder* | Opens the project in this folder (it is created when it does not exist). Without it, the project you had open last is opened. |
| `--autoplan` | After opening the project, loads its export and builds the plan right away (useful for quick checks). |
| `--selfcheck` | Checks that the single `.exe` is complete (its libraries and the FM reader unpack and load) and exits. Opens no window and changes nothing of your settings. Exit code 0 means fine. Used when building the release. |

## A patch file

A patch file (see [Patch files and the patcher](10-patches.md)) understands two options. All other arguments are ignored.

| Option | Meaning |
|--------|---------|
| `-q` | **Quiet mode.** Installs the patch and makes it live **without any window, popup or question**. Also accepted: `/q`, `--quiet` (upper or lower case). |
| `-F <folder>` | The Anstoss 2 folder: the folder where `anstoss2.exe` is located and which contains `DATA.DEF` and `DATA.VER`. Also accepted: `-F:folder`, `-F=folder`, `/F folder`, `--folder`. Without `-q` the folder is only filled into the window. |

Examples:

```
"A2G-Patch_FM26.exe" -q -F "C:\Games\ANSTOSS 2"
start /wait "" "A2G-Patch_FM26.exe" -q -F "C:\Games\ANSTOSS 2"
Start-Process -Wait -PassThru ".\A2G-Patch_FM26.exe" -ArgumentList '-q','-F','C:\Games\ANSTOSS 2'
```

The program has no console of its own; use `start /wait` (cmd) or `Start-Process -Wait` (PowerShell) to wait for it and read the exit code.

### What quiet mode does

- It uses the same data-set logic as the window ([Data sets (profiles) and rollback](09-profiles.md)): the patch becomes a data set named
  after the patch and is made live; the data that was there stays as its own data set. The **Original** data of the installation is
  stored automatically on the first install.
- **Same name:** a data set with the patch's name and other content (an older version of the patch) is **replaced**. If it already has
  exactly the content of the patch, it is only made live again, so running the patch twice changes nothing. The data set **Original**
  is never replaced; the patch is then installed under a numbered name.
- **Edited live files** are saved into the data set that is live now before anything is replaced, so nothing is lost.
- Without `-F` the folder used last by this patcher is taken, otherwise the automatic search of the converter. If nothing is found, it
  exits with code 1.
- The game must be closed (`anstoss2.exe`, `Editor.exe`); otherwise it exits with code 4.

### Exit codes

| Code | Meaning |
|------|---------|
| 0 | Installed and live (or already installed and now live). |
| 1 | No Anstoss 2 folder: no `-F`, and nothing remembered or found. |
| 2 | The folder is not an Anstoss 2 folder (`DATA.DEF` or `DATA.VER` missing). |
| 3 | The patch is damaged (its data cannot be read or a checksum does not match). |
| 4 | Any other problem, for example Anstoss 2 or its editor is running. |

Every problem also writes **one line of text** that says why: to the console that started the program, or to the redirected output
when the caller redirects it (installers can capture it). Example:
`Not an Anstoss 2 folder (DATA.DEF and DATA.VER are expected next to anstoss2.exe): C:\Games\Nowhere`.

If Anstoss 2 is in a protected folder (`Program Files`), run the installer or the patch elevated (as administrator).
