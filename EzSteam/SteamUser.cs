using SteamKit2;

namespace EzSteam
{
    public sealed class SteamUser
    {
        /// <summary>
        /// Provides access to the associated Bot instance.
        /// </summary>
        public readonly SteamBot Bot;

        /// <summary>
        /// Gets the user's Steam ID.
        /// </summary>
        public readonly SteamID Id;

        /// <summary>
        /// Gets the user's current name.
        /// </summary>
        public string DisplayName
        {
            get { return Bot.SteamFriends.GetFriendPersonaName(Id); }
        }

        /// <summary>
        /// Gets the user's current state.
        /// </summary>
        public EPersonaState State
        {
            get { return Bot.SteamFriends.GetFriendPersonaState(Id); }
        }

        /// <summary>
        /// Gets the user's current avatar.
        /// </summary>
        public byte[] Avatar
        {
            get { return Bot.SteamFriends.GetFriendAvatar(Id); }
        }

        /// <summary>
        /// Gets the game the user is currently playing.
        /// </summary>
        public GameID Playing
        {
            get { return Bot.SteamFriends.GetFriendGamePlayed(Id); }
        }

        /// <summary>
        /// Gets the name of the game the user is currently playing.
        /// </summary>
        public string PlayingName
        {
            get { return Bot.SteamFriends.GetFriendGamePlayedName(Id); }
        }

        internal SteamUser(SteamBot bot, SteamID id)
        {
            Bot = bot;
            Id = id;
        }
    }
}
