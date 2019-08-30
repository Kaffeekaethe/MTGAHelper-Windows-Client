﻿using MTGAHelper.Entity;
using MTGAHelper.Lib.Config.Users;
using MTGAHelper.Lib.IO.Reader.MtgaOutputLog;
using System;
using System.Collections.Generic;

namespace MTGAHelper.Lib.UserHistory
{
    public class DateSnapshotInfo
    {
        public DateTime Date { get; set; }
        //public ICollection<ConfigModelRawDeck> Decks { get; set; } = new ConfigModelRawDeck[0];
        public ICollection<ConfigModelRankInfo> RankInfo { get; set; } = new ConfigModelRankInfo[0];
        public Inventory Inventory { get; set; } = new Inventory();
        public Dictionary<int, int> Collection { get; set; } = new Dictionary<int, int>();
        public ICollection<MatchResult> Matches { get; set; } = new MatchResult[0];

        //public int Wins { get; set; }
        //public int Losses { get; set; }
        //public string ConstructedRank { get; set; }
        //public string LimitedRank { get; set; }
        public Dictionary<string, Outcomes> OutcomesByMode { get; set; } = new Dictionary<string, Outcomes>();

        public DateSnapshotInfo(DateTime date)
        {
            Date = date;
        }

        public DateSnapshotInfo()
        {
        }
    }

    //[Serializable]
    //public class ConfigModelUserHistoryEntry
    //{
    //    public Collection Collection { get; set; }
    //    public MtgaUserProfile UserProfile { get; set; } = new MtgaUserProfile();
    //}
}
