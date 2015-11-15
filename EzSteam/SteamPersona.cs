using SteamKit2;

namespace EzSteam
{
    public sealed class SteamPersona
    {
        /// <summary>
        /// Provides access to the associated Bot instance.
        /// </summary>
        public readonly SteamBot Bot;

        /// <summary>
        /// Gets the persona's Steam ID.
        /// </summary>
        public readonly SteamID Id;

        /// <summary>
        /// Gets the persona's current name.
        /// </summary>
        public string DisplayName => Bot.SteamFriends.GetFriendPersonaName(Id);

        /// <summary>
        /// Gets the persona's current state.
        /// </summary>
        public EPersonaState State => Bot.SteamFriends.GetFriendPersonaState(Id);

        /// <summary>
        /// Gets the persona's current avatar.
        /// </summary>
        public byte[] Avatar => Bot.SteamFriends.GetFriendAvatar(Id);

        /// <summary>
        /// Gets the ID of the game the persona is currently playing.
        /// </summary>
        public GameID Playing => Bot.SteamFriends.GetFriendGamePlayed(Id);

        /// <summary>
        /// Gets the name of the game the persona is currently playing.
        /// </summary>
        public string PlayingName => Playing != 0 ? Bot.SteamFriends.GetFriendGamePlayedName(Id) : null;

        internal SteamPersona(SteamBot bot, SteamID id)
        {
            Bot = bot;
            Id = id;
        }
    }
}
