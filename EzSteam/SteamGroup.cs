using System;
using System.Collections.Generic;
using System.Linq;
using SteamKit2;

namespace EzSteam
{
    public sealed class SteamGroup
    {
        public sealed class Member
        {
            public readonly SteamPersona Persona;
            public EClanPermission Rank { get; internal set; }

            public Member(SteamPersona persona, EClanPermission rank)
            {
                Persona = persona;
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
        public IEnumerable<Member> Members => _members.ToList();

        private readonly List<Member> _members;

        internal SteamGroup(SteamBot bot, SteamID id)
        {
            if (!id.IsClanAccount)
                throw new Exception("SteamClan from non-clan Id");

            Id = id;
            Bot = bot;
            _members = new List<Member>();
        }

        internal void SetRank(SteamID id, EClanPermission rank)
        {
            var member = _members.FirstOrDefault(m => m.Persona.Id == id);
            if (member == null)
                _members.Add(new Member(Bot.GetPersona(id), rank));
            else
                member.Rank = rank;
        }
    }
}
