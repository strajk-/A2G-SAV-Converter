using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using A2G.Convert.Plan;

namespace A2G.Convert.Writing
{
    /// <summary>The parts of a data set a partial install (and a partial patch) takes; everything else stays as it is in the live data.</summary>
    public sealed class InstallParts
    {
        /// <summary>The national name pools (JUGE*) and the twenty youth pools (JUGEND1-20).</summary>
        public bool NamePools;
        /// <summary>Avereine.sav, the non-league clubs.</summary>
        public bool Avereine;
        /// <summary>KLEINIG.SAV, which holds the year the game starts in.</summary>
        public bool StartYear;
        /// <summary>The countries (DEU, ENG, FRA, ITA, ESP): team, stadium, league and staff files and the crests (WAPPEN) of the country.</summary>
        public HashSet<string> Countries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public static class PartialInstall
    {
        /// <summary>The file filter for <see cref="InstallParts"/>: relative paths such as "DATA.DEF\LandDeut.sav" or "DATA.DEF\WAPPEN\VW_D1.BMP" are matched by file name.</summary>
        public static Func<string, bool> Filter(InstallParts parts)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (parts.NamePools) foreach (var f in A2Structure.NamePoolFiles()) files.Add(f);
            if (parts.Avereine) files.Add("Avereine.sav");
            if (parts.StartYear) files.Add("KLEINIG.SAV");
            var crestPatterns = new List<Regex>();
            foreach (var spec in A2Structure.Countries().Where(c => parts.Countries.Contains(c.Key)))
            {
                files.Add(spec.LandFile);
                files.Add(spec.StadFile);
                foreach (var f in A2Structure.StaffFiles(spec)) files.Add(f);
                foreach (var l in spec.Tiers.SelectMany(t => t.Leagues)) files.Add(l.LigaFile);
                // the crests of the country: VW_<letter><n>.BMP for its teams and <letter>0_00.BMP for a team beyond the list
                var letter = A2Structure.CrestLetter(spec.Key);
                if (letter != null)
                    crestPatterns.Add(new Regex("^(VW_" + letter + "[0-9]+|" + letter + "0_00)\\.BMP$", RegexOptions.IgnoreCase));
            }
            return rel =>
            {
                var name = Path.GetFileName(rel);
                return files.Contains(name) || crestPatterns.Any(r => r.IsMatch(name));
            };
        }
    }
}
