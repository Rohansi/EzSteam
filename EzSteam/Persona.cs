using SteamKit2;

namespace EzSteam
{
    public class Persona
    {
        /// <summary>
        /// Provides access to the associated Bot instance.
        /// </summary>
        public readonly Bot Bot;

        /// <summary>
        /// Gets the Steam ID of the persona.
        /// </summary>
        public readonly SteamID Id;

        /// <summary>
        /// Gets the current name of the persona.
        /// </summary>
        public string Name
        {
            get { return Bot.SteamFriends.GetFriendPersonaName(Id); }
        }

        /// <summary>
        /// Gets the current state of the persona.
        /// </summary>
        public EPersonaState State
        {
            get { return Bot.SteamFriends.GetFriendPersonaState(Id); }
        }

        /// <summary>
        /// Gets the current avatar of the persona.
        /// </summary>
        public byte[] Avatar
        {
            get { return Bot.SteamFriends.GetFriendAvatar(Id); }
        }

        /// <summary>
        /// Gets the game the persona is currently playing.
        /// </summary>
        public GameID Playing
        {
            get { return Bot.SteamFriends.GetFriendGamePlayed(Id); }
        }

        /// <summary>
        /// Gets the name of the game the persona is currently playing.
        /// </summary>
        public string PlayingName
        {
            get { return Bot.SteamFriends.GetFriendGamePlayedName(Id); }
        }

        internal Persona(Bot bot, SteamID id)
        {
            Bot = bot;
            Id = id;
        }
    }
}
