using System;
using System.Linq;
using A2G.Convert.Fm;

namespace A2G.Convert.Engine
{
    /// <summary>One set of kit: the stripe pattern and the colours of shirt, shorts and socks. A striped shirt uses the shorts colour for its stripes.</summary>
    public sealed class KitSet
    {
        public int Pattern, Shirt, Shorts, Socks;

        public int ShirtValue { get { return Pattern * 16 + Shirt; } }

        public KitSet Clone() { return new KitSet { Pattern = Pattern, Shirt = Shirt, Shorts = Shorts, Socks = Socks }; }

        public static KitSet FromShirtValue(int shirtValue, int shorts, int socks)
        {
            return new KitSet { Pattern = Math.Max(0, Math.Min(6, shirtValue / 16)), Shirt = Math.Max(0, Math.Min(12, shirtValue % 16)), Shorts = Math.Max(0, Math.Min(12, shorts)), Socks = Math.Max(0, Math.Min(12, socks)) };
        }
    }

    /// <summary>The home and away kit of a club.</summary>
    public sealed class ClubKit
    {
        public KitSet Home = new KitSet(), Away = new KitSet();

        public ClubKit Clone() { return new ClubKit { Home = Home.Clone(), Away = Away.Clone() }; }

        /// <summary>The kit as the eight numbers stored in the settings.</summary>
        public int[] ToArray()
        {
            return new[] { Home.Pattern, Home.Shirt, Home.Shorts, Home.Socks, Away.Pattern, Away.Shirt, Away.Shorts, Away.Socks };
        }

        public static ClubKit FromArray(int[] a)
        {
            if (a == null || a.Length != 8) return null;
            Func<int, int, int> clamp = (v, max) => Math.Max(0, Math.Min(max, v));
            return new ClubKit
            {
                Home = new KitSet { Pattern = clamp(a[0], 6), Shirt = clamp(a[1], 12), Shorts = clamp(a[2], 12), Socks = clamp(a[3], 12) },
                Away = new KitSet { Pattern = clamp(a[4], 6), Shirt = clamp(a[5], 12), Shorts = clamp(a[6], 12), Socks = clamp(a[7], 12) }
            };
        }
    }

    /// <summary>
    /// The kits of the game: 13 colours and 7 stripe patterns (read from Editor.exe, see docs\Dev\sav-format-verified.md). An FM save has no kit data, so the
    /// default kit of a club is made up from its id (a pleasing pair of colours); the user can change every part per club.
    /// </summary>
    public static class Kits
    {
        public const int ColourCount = 13, PatternCount = 7;

        /// <summary>The colours as the editor shows them (shaded photo of a shirt), index = colour number of the game.</summary>
        public static readonly int[][] Rgb =
        {
            new[] { 255, 255, 255 }, new[] { 183, 6, 29 }, new[] { 240, 234, 25 }, new[] { 0, 3, 146 }, new[] { 75, 191, 21 }, new[] { 0, 112, 0 },
            new[] { 0, 146, 133 }, new[] { 143, 222, 239 }, new[] { 138, 60, 24 }, new[] { 160, 18, 156 }, new[] { 252, 123, 13 }, new[] { 67, 67, 67 }, new[] { 142, 22, 77 }
        };

        // (primary, secondary) colour numbers of pleasing kits
        private static readonly int[][] Pairs =
        {
            new[] { 1, 0 }, new[] { 3, 0 }, new[] { 4, 0 }, new[] { 2, 11 }, new[] { 11, 1 }, new[] { 11, 2 }, new[] { 12, 7 }, new[] { 3, 2 },
            new[] { 10, 11 }, new[] { 0, 3 }, new[] { 0, 1 }, new[] { 9, 0 }, new[] { 6, 11 }, new[] { 3, 1 }, new[] { 5, 2 }, new[] { 1, 11 },
            new[] { 1, 3 }, new[] { 4, 11 }, new[] { 7, 3 }, new[] { 12, 2 }
        };

        private static int Hash(string uid, int salt)
        {
            unchecked
            {
                var h = 31 + salt * 104729;
                foreach (var c in uid ?? string.Empty) h = h * 31 + c;
                h ^= h >> 15; h *= 668265263; h ^= h >> 13;
                return h & 0x7FFFFFFF;
            }
        }

        /// <summary>The made-up default kit of a club: always different shirt colours for home and away.</summary>
        public static ClubKit Default(FmClub club)
        {
            var uid = club == null ? string.Empty : club.Uid;
            var pair = Pairs[Hash(uid, 1) % Pairs.Length];
            int primary = pair[0], secondary = pair[1];
            var roll = Hash(uid, 2) % 100;
            // plain 45, hoops 15, vertical stripes 15, halves 10, shoulders 5, chest band 5, sleeves 5
            var pattern = roll < 45 ? 0 : roll < 60 ? 2 : roll < 75 ? 1 : roll < 85 ? 3 : roll < 90 ? 4 : roll < 95 ? 5 : 6;
            var kit = new ClubKit();
            kit.Home = new KitSet { Pattern = pattern, Shirt = primary, Shorts = secondary, Socks = Hash(uid, 3) % 2 == 0 ? primary : secondary };
            // away: a white shirt (a coloured one when the home shirt is white); the home shirt colour is not used again for the away shirt
            var homeWhite = primary == 0;
            kit.Away = new KitSet
            {
                Pattern = Hash(uid, 4) % 100 < 75 ? 0 : 2,
                Shirt = homeWhite ? secondary : 0,
                Shorts = homeWhite ? 0 : primary,
                Socks = homeWhite ? secondary : 0
            };            return kit;
        }

        /// <summary>The kit written for a club: the user's choice, else the default.</summary>
        public static ClubKit For(ConversionSettings settings, FmClub club)
        {
            var o = settings == null || club == null ? null : settings.FindClubInfo(club.Uid);
            return (o == null ? null : ClubKit.FromArray(o.Kit)) ?? Default(club);
        }

        /// <summary>Stores the kit as the club's override (nothing is stored when it equals the default).</summary>
        public static void SetOverride(ConversionSettings settings, FmClub club, ClubKit kit)
        {
            var o = settings.FindClubInfo(club.Uid);
            var isDefault = Default(club).ToArray().SequenceEqual(kit.ToArray());
            if (o == null)
            {
                if (isDefault) return;
                o = new ClubInfoOverride { Uid = club.Uid };
                settings.ClubInfo.Add(o);
            }
            o.Kit = isDefault ? null : kit.ToArray();
            if (o.IsEmpty) settings.ClubInfo.Remove(o);
        }
    }
}
