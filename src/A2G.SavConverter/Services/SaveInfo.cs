using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace A2G.SavConverter.Services
{
    /// <summary>What "fmsave info" says about a Football Manager save (game version, build, in-game date).</summary>
    public sealed class SaveInfo
    {
        /// <summary>FM versions this converter is meant for: fmsave reads FM26 and FM27 saves.</summary>
        public static readonly int[] SupportedVersions = { 26, 27 };

        /// <summary>False when the file is not a Football Manager save fmsave can read.</summary>
        public bool Recognised { get; private set; }
        public string Error { get; private set; }
        /// <summary>The FM release number, e.g. 26.</summary>
        public int Version { get; private set; }
        public string Build { get; private set; }
        public string Database { get; private set; }
        public string InGameDate { get; private set; }

        public bool Supported { get { return Recognised && SupportedVersions.Contains(Version); } }

        public static SaveInfo Failed(string error)
        {
            return new SaveInfo { Recognised = false, Error = Regex.Replace(error ?? string.Empty, @"^\s*fmsave:\s*error:\s*", string.Empty, RegexOptions.IgnoreCase).Trim() };
        }

        /// <summary>
        /// Parses the lines "Game  FM26", "Build  26.3.2+2329565", "Database  26.2.0+0" and "In-game date  2025-07-21".
        /// </summary>
        public static SaveInfo Parse(string text)
        {
            var info = new SaveInfo();
            foreach (var line in (text ?? string.Empty).Split('\n'))
            {
                var m = Regex.Match(line.Trim(), @"^(Game|Build|Database|In-game date)\s{2,}(.+)$", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                var value = m.Groups[2].Value.Trim();
                switch (m.Groups[1].Value.ToLowerInvariant())
                {
                    case "game":
                        var v = Regex.Match(value, @"(\d+)");
                        if (v.Success)
                        {
                            int number = int.Parse(v.Groups[1].Value);
                            info.Version = number >= 2000 ? number - 2000 : number;   // "FM2024" style names
                            info.Recognised = true;
                        }
                        break;
                    case "build": info.Build = value; break;
                    case "database": info.Database = value; break;
                    case "in-game date": info.InGameDate = value; break;
                }
            }
            return info;
        }

        /// <summary>The build without the trailing "+number", e.g. "26.3.2".</summary>
        public string ShortBuild
        {
            get { return string.IsNullOrEmpty(Build) ? string.Empty : Build.Split('+')[0]; }
        }
    }
}
