# 12. Files and folders

Where the program keeps things, what you can copy, and how to start clean.

## Your user data

All of the program's own data is in your Windows user profile, in `%APPDATA%\A2G-SAV-Converter` (usually
`C:\Users\<you>\AppData\Roaming\A2G-SAV-Converter`; paste `%APPDATA%\A2G-SAV-Converter` into the Explorer address bar):

| Item | What it is |
|------|-----------|
| `Projects\<name>\` | One folder per project (see below). |
| `Library\` | The shared library (see below). |
| `language.txt`, `theme.txt` | The language and theme you chose. |
| `last-project.txt` | The project that is opened at the next start. |
| `patcher-install.txt` | The Anstoss 2 folder a patch used last. |

The built-in FM reader is unpacked once to `%LOCALAPPDATA%\A2G-SAV-Converter\fmsave\`. It can be deleted; it is unpacked again when
needed.

## A project folder

```
<project>\
    fm_export\    the CSV files exported from the FM save (players, clubs, nations, nation names, fixtures, staff)
    mapping\      settings.json (your options and club/player edits) and overrides.json (recorded plan edits: swaps and squad changes)
    Crests\       your own crest images, README.txt and _club-ids.csv
    Anstoss2\     the written data set: DATA.DEF\ and DATA.VER\ (with WAPPEN\ crests)
    project.json  the paths of the save and the Anstoss 2 folder for this project
```

- **To back up or share a project**, copy the whole folder. Someone else can open it with **Other folder...**
  ([Project and export](02-project-and-export.md)). The program looks the Anstoss 2 folder up again when the saved one does not exist on
  that PC; choose the FM save again if its path is different.
- **`Anstoss2\`** is the finished result. You can copy its two folders over an Anstoss 2 installation by hand, but installing through
  the program keeps your original data safe ([Write and install](08-write-and-install.md)).
- `settings.json` and `overrides.json` are plain text and best left to the program.

## The shared library

`%APPDATA%\A2G-SAV-Converter\Library` keeps what you did for **clubs**, independent of any project:

- `clubs.json` - club information (city, nickname, stadium name, capacity) and kits, by FM club id,
- `Crests\` - your own crests, by FM club id.

How it works:

- The first time a **project is opened**, it receives the library's content for every club it has no edit for. So a new project already
  has the texts, kits and crests you made before.
- From then on, every change in a project is stored in the library as well. Setting a club back to its defaults removes it from the
  library.
- Club ids stay the same in every FM save, so this works across new saves and new FM versions.

To start without your earlier club work, close the program and delete the `Library` folder.

## In the Anstoss 2 folder

When you install, the program adds to your Anstoss 2 folder (and to nothing else):

- `Profiles\` - the data sets, including **Original** ([Data sets and rollback](09-profiles.md)),
- `A2G_PROFILE.json` - which data set is live.

It also changes `DATA.DEF` and `DATA.VER` (the live data). Your saved games and settings are not touched.

## Starting over

- **A fresh project:** **New project...** on tab 1.
- **Forget the program's settings:** close the program and delete `%APPDATA%\A2G-SAV-Converter`. Projects live there too, so copy
  the ones you want to keep first.
- **Remove the program:** delete `A2G-SAV-Converter.exe`. It leaves no installation behind. The data sets in your Anstoss 2 folder stay
  until you switch to **Original** and delete the `Profiles` folder yourself.

Next: [Troubleshooting and FAQ](13-troubleshooting.md).
