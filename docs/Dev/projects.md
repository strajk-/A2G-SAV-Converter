# Projects

A project is one folder with everything of one conversion: the exported FM data (`fm_export`), the settings and edits (`mapping`), the
written `.sav` files (`Anstoss2`) and the crest folder. Projects are named and kept side by side:

- They live in `%APPDATA%\A2G-SAV-Converter\Projects\<name>`. The project list (first tab, "Project") shows them; selecting one opens it.
  The program remembers the last open project and opens it at the next start (`last-project.txt` next to the projects folder).
- **New project...** asks for a name (suggested from the FM version, e.g. FM26, FM26-2) and creates the folder. **Rename...** and **Delete...**
  work on the open project; deleting asks first and does not touch data sets already installed in Anstoss 2.
  **Other folder...** opens a project from anywhere; such a folder is shown with its path and is never renamed or deleted by the program.
- The name of the project is the default name of the data set (profile) when you install and of the patch file: the project FM26 installs
  as the profile FM26 and makes `A2G-Patch_FM26.exe`. The profile list shows which project a data set came from ("Project FM26").
- The first project, `Default`, is what older versions used for everything. It keeps working and can be renamed (a profile named after a
  project is only a name: renaming a project does not rename an installed data set). The shared library (`docs\Dev\conversion-rules.md`)
  carries club information and crests from project to project.
