# Install profiles

The converter and the patcher (`patches.md`) manage data sets ("profiles") on an Anstoss 2 install, so installing a new data set never
loses the previous one and the user can swap back and forth. Both use the same code (`A2G.Convert/Profiles/ProfileManager.cs`).

## Layout inside the Anstoss 2 install folder
```
<install>\
  DATA.DEF\ DATA.VER\        the live data the game reads (the files this tool replaces)
  A2G_PROFILE.json           marker: which profile is currently live
  Profiles\
    Original\                the untouched shipped data (created on first use)
      DATA.DEF\ DATA.VER\
      profile.json           name, created, source, file manifest (relative path, size, SHA-256)
    <other profiles>\ ...
```
`A2G_PROFILE.json` holds the active profile name, its id and when it was activated, so the app always knows what is live.

## Operations
- **First use:** no marker -> the live DATA.DEF / DATA.VER are the original data. Copy them to `Profiles\Original`,
  verify the copy against hashes, then write the marker.
- **Install a new data set:** if the live files were edited, the user chooses to save them into the active profile or to discard
  them. The new data is then stored as its own profile, copied over the live folders and the marker updated. A name that exists
  already can be replaced (after asking; never `Original`): the new data is built next to the old profile and only takes its place when
  everything worked.
- **Switch profile:** save the live files back into the active profile, then copy the chosen profile's files live.
- **Rename / delete profile:** `Original` can be renamed but not deleted; deleting asks for confirmation.
- Scope: DATA.DEF and DATA.VER only (including the WAPPEN crest folder). Game saves and settings in the install root
  are never touched.

## Safety rules
- Copy first, verify (size + SHA-256), only then replace; never delete the only copy of anything.
- Refuse to run while `anstoss2.exe` or `Editor.exe` is running.
- Replace via a temporary folder and rename, so an interrupted switch can be detected and repaired from the manifest.
- Warn when the live files differ from the active profile's manifest (edited by hand or by the Editor) and offer to
  save them into that profile or into a new one.
- The project folder's `Anstoss2\` output is installed through this same mechanism ("Install into game").
