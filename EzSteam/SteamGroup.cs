﻿using System;
using System.Collections.Generic;
using System.Linq;
using SteamKit2;

namespace EzSteam
{
    public enum SteamGroupRank : byte
    {
        Guest = 0,
        Owner = 1,
        Officer = 2,
        Member = 4,
        Moderator = 8
    }

    public sealed class SteamGroup
    {
        public sealed class Member
        {
            public readonly SteamUser User;
            public SteamGroupRank Rank { get; internal set; }

            public Member(SteamUser user, SteamGroupRank rank)
            {
                User = user;
                Rank = rank;
            }
        }

        /// <summary>
        /// Provides access to the associated Bot instance.
        /// </summary>
        public readonly SteamBot Bot;

        /// <summary>
        /// Gets the group's Steam ID.
        /// </summary>
        public readonly SteamID Id;

        /// <summary>
        /// Gets the group's name.
        /// </summary>
        public string Name { get; internal set; }

        /// <summary>
        /// Gets the group's current avatar.
        /// </summary>
        public byte[] Avatar { get; internal set; }

        /// <summary>
        /// Gets the list of known members in this group. This will most likely not contain all
        /// members in the group.
        /// </summary>
        public IEnumerable<Member> Members { get { return _members.ToList(); } }

        private readonly List<Member> _members;
         
        internal SteamGroup(SteamBot bot, SteamID id)
        {
            if (!id.IsClanAccount)
                throw new Exception("SteamClan from non-clan Id");

            Id = id;
            Bot = bot;
            _members = new List<Member>();
        }

        internal void SetRank(SteamID id, SteamGroupRank rank)
        {
            var member = _members.FirstOrDefault(m => m.User.Id == id);
            if (member == null)
                _members.Add(new Member(Bot.GetUser(id), rank));
            else
                member.Rank = rank;
        }
    }
}