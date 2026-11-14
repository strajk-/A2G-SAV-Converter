using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using A2G.Convert.Fm;

namespace A2G.Convert.Engine
{
    /// <summary>
    /// The real kits of clubs, built into the program (Data\club_kits.json, made by tools\make-kit-database.py from an export of the
    /// kit colours of the FM database). Clubs are found by their FM database id (the unique_id column of the save's clubs table).
    /// A club the database does not know gets a made-up kit.
    /// </summary>
    public static class KitDatabase
    {
        [DataContract]
        private sealed class Root
        {
            /// <summary>Per database id: home pattern, shirt, shorts, socks, then the same for away (the 4 home numbers only when the club has no away kit).</summary>
            [DataMember(Name = "kits")] public Dictionary<string, int[]> Kits { get; set; }
        }

        private static Dictionary<string, int[]> _kits;

        private static Dictionary<string, int[]> Load()
        {
            if (_kits != null) return _kits;
            var result = new Dictionary<string, int[]>();
            try
            {
                using (var stream = typeof(KitDatabase).Assembly.GetManifestResourceStream("Data.club_kits.json"))
                {
                    if (stream != null)
                    {
                        var settings = new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };
                        var root = (Root)new DataContractJsonSerializer(typeof(Root), settings).ReadObject(stream);
                        if (root != null && root.Kits != null) result = root.Kits;
                    }
                }
            }
            catch (Exception) { /* a damaged or missing file only means made-up kits */ }
            _kits = result;
            return _kits;
        }

        public static int Count { get { return Load().Count; } }

        /// <summary>The database kit of the club (a fresh object), or null when the club is not in the database.</summary>
        public static ClubKit Find(FmClub club)
        {
            int[] values;
            if (club == null || string.IsNullOrEmpty(club.UniqueId) || !Load().TryGetValue(club.UniqueId, out values)) return null;
            if (values.Length == 8) return ClubKit.FromArray(values);
            if (values.Length != 4) return null;
            var home = ClubKit.FromArray(new[] { values[0], values[1], values[2], values[3], 0, 0, 0, 0 });
            if (home != null) home.Away = null;   // no away kit in the database
            return home;
        }
    }
}
