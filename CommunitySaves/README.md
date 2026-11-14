# Community saves

Football Manager saves that the converter offers under **Community saves** (Project & Export tab). Each save lives in a group folder:

```
CommunitySaves/
  2026/
    2026_07.fm      the save (.fm)
    settings.json   optional defaults for every save in this folder
```

`settings.json` currently knows one value, the year the Anstoss 2 game should start in (it replaces the year of the save, for example
2026 for an FM26 update save dated 2025):

```json
{
  "StartYear": 2026
}
```

The converter lists the files through the GitHub API (`git/trees`), downloads a save from `raw.githubusercontent.com` into
`%APPDATA%\A2G-SAV-Converter\CommunitySaves` and compares the git blob hash of the repository with the stored one, so an unchanged
save is not downloaded again. Files must stay below GitHub's 100 MB limit. The files have to be on the `main` branch.
