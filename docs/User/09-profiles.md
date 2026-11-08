# 9. Data sets (profiles) and rollback

Installing a conversion must never cost you your original game. The converter and the patcher therefore manage **data sets** (the
program also calls them **profiles**): complete copies of the game's data that you can switch between at any time.

## How it works

Inside your Anstoss 2 folder the program keeps:

```
<Anstoss 2 folder>\
    DATA.DEF\ DATA.VER\       the live data - what the game reads right now
    A2G_PROFILE.json          which data set is live
    Profiles\
        Original\             the data your installation had before you started
        FM26\                 a data set you installed
        ...
```

- **Original** is stored automatically the first time you install something. It is the untouched data of your installation.
- Every install creates a new data set and makes it live (copies it over `DATA.DEF` and `DATA.VER`).
- **Switching** to another data set copies its files over the live folders.
- Only `DATA.DEF` and `DATA.VER` (including the crests in `WAPPEN`) are managed. Your saved games and game settings in the Anstoss 2
  folder are never touched.

## The data set list (tab 7)

The line above the list tells you what is live: `Live profile: FM26`. If the live files were changed since (for example by the game's
editor) it adds `(live files were changed: 3 file(s) differ)`. A new installation shows: "This install has not been managed yet. The
first install stores the shipped data as the 'Original' profile."

| Column | Meaning |
|--------|---------|
| **Profile** | The name of the data set. |
| **Live** | `yes` for the data set that is live now. |
| **Created (UTC)** | When it was created. |
| **Files** | The number of files it contains. |
| **Source** | Where it came from: `Project FM26` for an install from one of your projects, `Patch: <name>` for a data set installed by a patch file, or a note (the Original says "Shipped data found in the install on first use"). |

Buttons:

| Button | What it does |
|--------|--------------|
| **Activate selected** | Makes the selected data set live. |
| **Rename** | Gives the selected data set a new name. |
| **Delete** | Deletes the selected data set (after asking). |
| **Refresh** | Reads the list again. |

## Going back to the original data

1. Close Anstoss 2 and its editor.
2. On tab 7, select **Original** in the list and press **Activate selected** (or, with a patch file, pick it in the patcher and press
   **Switch to the selected data set**).
3. Answer the question about changed live files if it appears.
4. The log says `Profile 'Original' is now live.`

Switching back and forth is always possible; no data set is lost by switching.

## Edited live files

If the live files differ from the data set that is live (you used the game's editor, for example), switching or installing asks what to
do with those changes:

- **Yes** - save the changes into that data set first,
- **No** - discard them,
- **Cancel** - do nothing.

## Rules of the program

- The program refuses to change data sets while **Anstoss 2 or its editor is running**.
- It copies first, **verifies every file** (size and checksum), and only then replaces the live folders; the swap is done by renaming
  folders, so an interrupted switch cannot leave half-written data.
- **Original** cannot be deleted and cannot be replaced by an install.
- The data set that is **live** cannot be deleted; activate another one first.
- Installing under the name of an existing data set replaces it after you confirm; if anything fails the old data set stays as it was.

## Where the files are

The files of every data set are ordinary folders in `Profiles\<name>` inside the Anstoss 2 folder, so nothing is hidden. Please switch
with the program (or the patcher) rather than copying by hand: it checks every file and keeps track of which data set is live.

Next: [Patch files and the patcher](10-patches.md).
