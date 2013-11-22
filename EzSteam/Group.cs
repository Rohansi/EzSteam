using System;
using System.Collections.Generic;
using System.Linq;
using SteamKit2;

namespace EzSteam
{
    public enum ClanRank : byte
    {
        Guest = 0,
        Owner = 1,
        Officer = 2,
        Member = 4,
        Moderator = 8
    }

    public class Group
    {
        public class Member
        {
            public readonly SteamID Id;
            public ClanRank Rank { get; internal set; }

            public Member(SteamID id, ClanRank rank)
            {
                Id = id;
                Rank = rank;
            }
        }

        public readonly SteamID Id;
        public string Name { get; internal set; }
        public byte[] Avatar { get; internal set; }
        public IEnumerable<Member> Members { get { return members; } } 

        private readonly List<Member> members;
         
        internal Group(SteamID id)
        {
            if (!id.IsClanAccount)
                throw new Exception("SteamClan from non-clan Id");

            Id = id;
            members = new List<Member>();
        }

        internal void SetRank(SteamID id, ClanRank rank)
        {
            var member = members.FirstOrDefault(m => m.Id == id);
            if (member == null)
                members.Add(new Member(id, rank));
            else
                member.Rank = rank;
        }
    }
}
