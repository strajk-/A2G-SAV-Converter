using System.Collections.Generic;
using System.Linq;

namespace A2G.Convert.Plan
{
    /// <summary>One league file of Anstoss 2 (a LIGA*.SAV) and the number of teams it holds.</summary>
    public sealed class A2LeagueSpec
    {
        public A2LeagueSpec(string name, string ligaFile, int size)
        {
            Name = name; LigaFile = ligaFile; Size = size;
        }
        public string Name { get; private set; }
        public string LigaFile { get; private set; }
        public int Size { get; private set; }
    }

    /// <summary>A division level of a country. The German third tier consists of four parallel leagues (Regionalligen).</summary>
    public sealed class A2TierSpec
    {
        public A2TierSpec(string name, params A2LeagueSpec[] leagues)
        {
            Name = name; Leagues = leagues.ToList();
        }
        public string Name { get; private set; }
        public List<A2LeagueSpec> Leagues { get; private set; }
        public int Size { get { return Leagues.Sum(l => l.Size); } }
    }

    /// <summary>The fixed structure of one Land*.sav country in Anstoss 2 (see docs\Dev\sav-format-verified.md).</summary>
    public sealed class A2CountrySpec
    {
        public string Key { get; set; }
        /// <summary>Name of the nation in the FM install's language database (English).</summary>
        public string FmNationName { get; set; }
        /// <summary>Name of the nation in LAENDER.SAV (German).</summary>
        public string A2NationName { get; set; }
        public string LandFile { get; set; }
        public string StadFile { get; set; }
        public List<A2TierSpec> Tiers { get; set; }
        /// <summary>Teams in the Land file that are in no league (promotion/relegation pool).</summary>
        public int SpareTeams { get; set; }
        /// <summary>Spare club names listed at the end of the Land file.</summary>
        public int TailClubNames { get; set; }

        public int TeamCount { get { return Tiers.Sum(t => t.Size) + SpareTeams; } }
    }

    public static class A2Structure
    {
        /// <summary>The ending the original files use for a country: "Deut", "Engl", "Fran", "Ital" or "Span".</summary>
        public static string CountrySuffix(string key)
        {
            switch (key)
            {
                case "DEU": return "Deut";
                case "ENG": return "Engl";
                case "FRA": return "Fran";
                case "ITA": return "Ital";
                default: return "Span";
            }
        }

        /// <summary>The file names of a country's people: trainers, assistants, managers, referees and prominent persons.</summary>
        /// <summary>The letter of the crest files of a country (WAPPEN\VW_{letter}{n}.BMP): D, E, F, I or S; null for other keys.</summary>
        public static string CrestLetter(string countryKey)
        {
            switch (countryKey) { case "DEU": return "D"; case "ENG": return "E"; case "FRA": return "F"; case "ITA": return "I"; case "ESP": return "S"; default: return null; }
        }

        public static IEnumerable<string> StaffFiles(A2CountrySpec spec)
        {
            foreach (var prefix in new[] { "Trai", "TTra", "Mana", "NSch", "Prom" })
                yield return prefix + CountrySuffix(spec.Key) + ".sav";
        }

        /// <summary>All name pool files (national and youth pools).</summary>
        public static IEnumerable<string> NamePoolFiles()
        {
            foreach (var key in new[] { "DEU", "ENG", "FRA", "ITA", "ESP" })
                yield return "JUGE" + CountrySuffix(key).ToUpperInvariant() + ".SAV";
            for (var i = 1; i <= 20; i++) yield return "JUGEND" + i + ".SAV";
        }

        public static List<A2CountrySpec> Countries()
        {
            return new List<A2CountrySpec>
            {
                new A2CountrySpec
                {
                    Key = "DEU", FmNationName = "Germany", A2NationName = "Deutschland",
                    LandFile = "LandDeut.sav", StadFile = "StadDeut.sav", SpareTeams = 16, TailClubNames = 40,
                    Tiers = new List<A2TierSpec>
                    {
                        new A2TierSpec("1. Liga", new A2LeagueSpec("1. Liga", "LIGA1DEU.SAV", 18)),
                        new A2TierSpec("2. Liga", new A2LeagueSpec("2. Liga", "LIGA2DEU.SAV", 18)),
                        new A2TierSpec("Regionalliga",
                            new A2LeagueSpec("Regionalliga Nord", "LIGARLNO.SAV", 18),
                            new A2LeagueSpec("Regionalliga Ost", "LIGARLOS.SAV", 18),
                            new A2LeagueSpec("Regionalliga Sued", "LIGARLSU.SAV", 18),
                            new A2LeagueSpec("Regionalliga Suedwest", "LIGARLSW.SAV", 18))
                    }
                },
                new A2CountrySpec
                {
                    Key = "ENG", FmNationName = "England", A2NationName = "England",
                    LandFile = "LANDENGL.SAV", StadFile = "STADENGL.SAV", SpareTeams = 7, TailClubNames = 40,
                    Tiers = new List<A2TierSpec>
                    {
                        new A2TierSpec("Division 1", new A2LeagueSpec("Division 1", "LIGA1ENG.SAV", 20)),
                        new A2TierSpec("Division 2", new A2LeagueSpec("Division 2", "LIGA2ENG.SAV", 24)),
                        new A2TierSpec("Division 3", new A2LeagueSpec("Division 3", "LIGA3ENG.SAV", 24))
                    }
                },
                new A2CountrySpec
                {
                    Key = "FRA", FmNationName = "France", A2NationName = "Frankreich",
                    LandFile = "LANDFRAN.SAV", StadFile = "STADFRAN.SAV", SpareTeams = 6, TailClubNames = 40,
                    Tiers = new List<A2TierSpec>
                    {
                        new A2TierSpec("Division 1", new A2LeagueSpec("Division 1", "LIGA1FRA.SAV", 18)),
                        new A2TierSpec("Division 2", new A2LeagueSpec("Division 2", "LIGA2FRA.SAV", 22))
                    }
                },
                new A2CountrySpec
                {
                    Key = "ITA", FmNationName = "Italy", A2NationName = "Italien",
                    LandFile = "LANDITAL.SAV", StadFile = "STADITAL.SAV", SpareTeams = 6, TailClubNames = 40,
                    Tiers = new List<A2TierSpec>
                    {
                        new A2TierSpec("Serie A", new A2LeagueSpec("Serie A", "LIGA1ITA.SAV", 18))
                    }
                },
                new A2CountrySpec
                {
                    Key = "ESP", FmNationName = "Spain", A2NationName = "Spanien",
                    LandFile = "LANDSPAN.SAV", StadFile = "STADSPAN.SAV", SpareTeams = 6, TailClubNames = 40,
                    Tiers = new List<A2TierSpec>
                    {
                        new A2TierSpec("Primera", new A2LeagueSpec("Primera", "LIGA1SPA.SAV", 20))
                    }
                }
            };
        }
    }
}
