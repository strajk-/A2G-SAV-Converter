using System;
using A2G.Convert.Engine;
using A2G.Convert.Fm;

namespace A2G.Convert.Writing
{
    /// <summary>
    /// The city, nickname and stadium name written for a club. FM saves have none of these, so the defaults are generated from
    /// the club name; the user can change them per club (<see cref="ConversionSettings.ClubInfo"/>).
    /// </summary>
    public static class ClubTexts
    {
        public static string DefaultCity(FmClub club) { return SavWriter.CityFor(club); }
        public static string DefaultNickname(FmClub club) { return NameShortener.Fit(SavWriter.CityFor(club), ClubNamer.MaxNickname); }
        public static string DefaultStadium(FmClub club, int max) { return NameShortener.Fit(SavWriter.CityFor(club) + " Stadion", max); }

        /// <summary>
        /// Stores what differs from the generated defaults as the club's override (an empty text or one equal to the default means
        /// "use the default"; the nickname and stadium defaults follow the city). Returns true when the effective texts changed.
        /// </summary>
        public static bool SetOverride(ConversionSettings s, FmClub club, string city, string nickname, string stadium)
        {
            return SetOverride(s, club, city, nickname, stadium, Capacity(club, s));
        }

        public const int MinCapacity = 3000, MaxCapacity = 110000;

        /// <summary>Stadium capacity from the club's reputation (FM has no stadium data here): 3,000 up to about 65,000.</summary>
        public static int DefaultCapacity(FmClub club)
        {
            var x = Math.Max(0, Math.Min(1.0, (club.Reputation - 1000) / 8000.0));
            var v = (int)Math.Round((3000 + 62000 * x * x) / 100.0) * 100;
            return Math.Max(MinCapacity, Math.Min(MaxCapacity, v));
        }

        /// <summary>The capacity: the user's value, else the reputation-based default.</summary>
        public static int Capacity(FmClub club, ConversionSettings s)
        {
            var o = s == null ? null : s.FindClubInfo(club.Uid);
            if (o != null && o.Capacity > 0) return Math.Max(MinCapacity, Math.Min(MaxCapacity, o.Capacity));
            return DefaultCapacity(club);
        }

        public static bool SetOverride(ConversionSettings s, FmClub club, string city, string nickname, string stadium, int capacity)
        {
            var before = City(club, s, ClubNamer.MaxCity) + "|" + Nickname(club, s) + "|" + Stadium(club, s, ClubNamer.MaxStadium) + "|" + Capacity(club, s);
            city = (city ?? string.Empty).Trim();
            nickname = (nickname ?? string.Empty).Trim();
            stadium = (stadium ?? string.Empty).Trim();
            var o = s.FindClubInfo(club.Uid);
            if (o == null) { o = new ClubInfoOverride { Uid = club.Uid }; s.ClubInfo.Add(o); }
            var defaultCity = DefaultCity(club);
            o.City = city.Length == 0 || city == defaultCity ? null : city;
            var cityNow = o.City ?? defaultCity;
            o.Nickname = nickname.Length == 0 || nickname == NameShortener.Fit(cityNow, ClubNamer.MaxNickname) ? null : nickname;
            o.Stadium = stadium.Length == 0 || stadium == NameShortener.Fit(cityNow + " Stadion", ClubNamer.MaxStadium) ? null : stadium;
            capacity = Math.Max(MinCapacity, Math.Min(MaxCapacity, capacity));
            o.Capacity = capacity == DefaultCapacity(club) ? 0 : capacity;
            if (o.City == null && o.Nickname == null && o.Stadium == null && o.Capacity == 0 && o.Kit == null) s.ClubInfo.Remove(o);
            var after = City(club, s, ClubNamer.MaxCity) + "|" + Nickname(club, s) + "|" + Stadium(club, s, ClubNamer.MaxStadium) + "|" + Capacity(club, s);
            return before != after;
        }
        private static string Pick(string custom, string fallback, int max)
        {
            return string.IsNullOrWhiteSpace(custom) ? fallback : NameShortener.Fit(custom.Trim(), max);
        }

        public static string City(FmClub club, ConversionSettings s, int max)
        {
            var o = s == null ? null : s.FindClubInfo(club.Uid);
            return Pick(o == null ? null : o.City, NameShortener.Fit(SavWriter.CityFor(club), max), max);
        }

        /// <summary>The nickname: the user's text, else the city (the user's city when he changed it).</summary>
        public static string Nickname(FmClub club, ConversionSettings s)
        {
            var o = s == null ? null : s.FindClubInfo(club.Uid);
            if (o != null && !string.IsNullOrWhiteSpace(o.Nickname)) return NameShortener.Fit(o.Nickname.Trim(), ClubNamer.MaxNickname);
            return NameShortener.Fit(City(club, s, ClubNamer.MaxCity), ClubNamer.MaxNickname);
        }

        /// <summary>The stadium name: the user's text, else "[city] Stadion".</summary>
        public static string Stadium(FmClub club, ConversionSettings s, int max)
        {
            var o = s == null ? null : s.FindClubInfo(club.Uid);
            if (o != null && !string.IsNullOrWhiteSpace(o.Stadium)) return NameShortener.Fit(o.Stadium.Trim(), max);
            return NameShortener.Fit(City(club, s, ClubNamer.MaxCity) + " Stadion", max);
        }
    }
}
