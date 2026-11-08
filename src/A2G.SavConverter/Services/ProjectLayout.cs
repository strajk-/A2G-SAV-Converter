using A2G.SavConverter.Localization;
using System;
using System.IO;

namespace A2G.SavConverter.Services
{
    /// <summary>
    /// The folder a conversion project lives in. Everything the tool produces stays inside it, so the
    /// whole folder can be zipped and shared. The "Anstoss2" subfolder mirrors the game install:
    /// copying its contents over the install folder replaces the data 1:1.
    /// </summary>
    public sealed class ProjectLayout
    {
        public ProjectLayout(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException(Loc.T(26008), "root");
            Root = Path.GetFullPath(root);
        }

        public string Root { get; private set; }

        /// <summary>Intermediate CSV files exported from the FM save.</summary>
        public string FmExport { get { return Path.Combine(Root, "fm_export"); } }

        /// <summary>User-editable mapping tables (FM nation to A2 nation, club selection, ...).</summary>
        public string Mapping { get { return Path.Combine(Root, "mapping"); } }

        /// <summary>Community / own crests: one image per FM club, named by the club id (see the README.txt written into it).</summary>
        public string Crests { get { return Path.Combine(Root, "Crests"); } }

        /// <summary>The deliverable: mirrors the Anstoss 2 install folder (DATA.DEF, DATA.VER).</summary>
        public string Anstoss2 { get { return Path.Combine(Root, "Anstoss2"); } }

        public string DataDef { get { return Path.Combine(Anstoss2, "DATA.DEF"); } }
        public string DataVer { get { return Path.Combine(Anstoss2, "DATA.VER"); } }

        /// <summary>Default project location: %APPDATA%\A2G-SAV-Converter\Projects\Default (next to the library and the profiles).</summary>
        public static string DefaultRoot
        {
            get
            {
                var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2G-SAV-Converter", "Projects", "Default");
                // earlier versions kept the default project next to the application: move it over once
                var old = Path.Combine(Path.GetDirectoryName(typeof(ProjectLayout).Assembly.Location), "Projects", "Default");
                try
                {
                    if (!Directory.Exists(target) && Directory.Exists(old))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        Directory.Move(old, target);
                    }
                }
                catch (Exception) { if (!Directory.Exists(target) && Directory.Exists(old)) return old; }
                return target;
            }
        }

        public void Ensure()
        {
            Directory.CreateDirectory(FmExport);
            Directory.CreateDirectory(Mapping);
            Directory.CreateDirectory(DataDef);
            Directory.CreateDirectory(DataVer);
        }
    }
}
