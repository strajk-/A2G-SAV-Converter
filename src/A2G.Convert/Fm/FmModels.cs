using System.Collections.Generic;

namespace A2G.Convert.Fm
{
    /// <summary>A nation of the FM save: the save-local id, the database id and (from the FM install) its name.</summary>
    public sealed class FmNation
    {
        public int SaveId { get; set; }
        public int DatabaseId { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }
    }

    public sealed class FmClub
    {
        public string Uid { get; set; }
        public string Name { get; set; }
        public string ShortName { get; set; }
        public int NationId { get; set; }
        public int Reputation { get; set; }
        /// <summary>Final position of the last finished season in the club's own league (0 = none).</summary>
        public int LastLeaguePosition { get; set; }
        /// <summary>
        /// Total capacity (seated and standing) of the ground the club plays its home matches at, from the save's stadium table
        /// (stadiums.csv); 0 when the save has no ground for the club (only clubs whose matches are in the save have one).
        /// </summary>
        public int StadiumCapacity { get; set; }
        /// <summary>Team id of the club's first team (slot 0), or -1.</summary>
        public int FirstTeamId { get; set; }
        /// <summary>True for men's clubs that are not a reserve/affiliate of another club.</summary>
        public bool IsMainMensClub { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public sealed class FmPlayer
    {
        public string Uid { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string CommonName { get; set; }
        public string DisplayName { get; set; }
        public int Age { get; set; }
        public int NationId { get; set; }
        public int CurrentAbility { get; set; }
        public int PotentialAbility { get; set; }
        public string ClubUid { get; set; }
        public int TeamId { get; set; }
        public int HeightCm { get; set; }
        public string BirthDate { get; set; }
        public int ReputationCurrent { get; set; }
        public int ReputationWorld { get; set; }
        /// <summary>FM player traits as text ("Tries To Play Way Out Of Trouble"), possibly empty.</summary>
        public List<string> Traits { get; set; }
        /// <summary>Position ratings 0..20 by FM position code (gk, sw, dl, dc, dr, dm, ml, mc, mr, aml, amc, amr, stc, wbl, wbr).</summary>
        public Dictionary<string, int> PositionRatings { get; set; }
        /// <summary>Attributes, personality and foot strengths scaled 1..20, keyed by name without prefix (e.g. "pace").</summary>
        public Dictionary<string, int> Attributes { get; set; }

        public int Attribute(string name)
        {
            int v;
            return Attributes.TryGetValue(name, out v) ? v : 0;
        }

        public bool IsGoalkeeper
        {
            get
            {
                int gk;
                if (!PositionRatings.TryGetValue("gk", out gk)) return false;
                return gk >= 15;
            }
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    /// <summary>A non-playing staff member (coach, scout, physio, ...). FM26 saves do not say which job a person has.</summary>
    public sealed class FmStaff
    {
        public string Uid { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string DisplayName { get; set; }
        public int Age { get; set; }
        public int NationId { get; set; }
        public string ClubUid { get; set; }
        public int CurrentAbility { get; set; }
        /// <summary>Coaching preference "hardness of training" (1-20).</summary>
        public int TrainingHardness { get; set; }
        public int Temperament { get; set; }
    }

    /// <summary>A league of the FM save, derived from the fixtures (see FmLeagueDetector).</summary>
    public sealed class FmLeague
    {
        public int CompetitionId { get; set; }
        public int NationId { get; set; }
        public double AverageReputation { get; set; }
        public List<FmClub> Clubs { get; set; }
        /// <summary>1 = top division of its nation.</summary>
        public int Tier { get; set; }
    }
}
