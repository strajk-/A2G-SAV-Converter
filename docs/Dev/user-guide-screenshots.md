# Screenshots of the user guide

The pictures in `docs/User/images` are made by the program itself, so they can be regenerated after any UI change:

```
A2G-SAV-Converter.exe "<project folder>" --screenshots=<output folder>
```

For example with the output folder `docs\User\images`. The tour (`src/A2G.SavConverter/MainWindow.Screenshots.cs`):

- opens the project, builds the plan, and walks through every tab and the important windows (team information with the reserve view, player
  search, season results for a league and for the non-league clubs, crest painter, patcher),
- draws each window **off-screen** (`RenderTargetBitmap`): no screen capture, no mouse or keyboard automation,
- uses English and the light theme (plus one dark picture) and puts the language and theme preferences back afterwards,
- only looks at the project: nothing is written to it,
- closes the program when it is done.

Use a project with an export (the pictures show its clubs and players). The log area at the bottom of the main window shows the project
path: look through the pictures before publishing them and cover anything private.

The file names are the ones referenced by the pages of `docs/User`; keep them when adding pictures.
