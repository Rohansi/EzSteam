using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SteamKit2;

namespace EzSteam
{
    public enum SteamRoomLeaveReason
    {
        JoinFailed,
        JoinTimeout,

        Left,
        Disconnected,
        Kicked,
        Banned
    }

    public sealed class SteamRoom
    {
        public delegate void EnterEvent(SteamRoom room);
        public delegate void LeaveEvent(SteamRoom room, SteamRoomLeaveReason reason);
        public delegate void MessageEvent(SteamRoom room, SteamUser user, string message);
        public delegate void UserEnterEvent(SteamRoom room, SteamUser user);
        public delegate void UserLeaveEvent(SteamRoom room, SteamUser user, SteamRoomLeaveReason reason, SteamUser sourceUser = null);

        /// <summary>
        /// Provides access to the associated Bot instance.
        /// </summary>
        public readonly SteamBot Bot;

        /// <summary>
        /// Gets the SteamID of the room.
        /// </summary>
        public readonly SteamID Id;

        /// <summary>
        /// Returns true while the room is available for use.
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Gets the title (text that shows up in Steam tabs) of the room.
        /// </summary>
        public string Title
        {
            get
            {
                if (Id.IsIndividualAccount)
                    return Bot.SteamFriends.GetFriendPersonaName(Id);
                if (Id.AccountInstance == 3260)
                    return string.Join(" + ", Users.Select(u => u.DisplayName));
                var clan = Bot.SteamClans.Get(Id);
                return clan != null ? clan.Name : "[unknown]";
            }
        }

        /// <summary>
        /// Gets a list of the users currently in the room.
        /// </summary>
        public IEnumerable<SteamUser> Users
        {
            get { return _users.Select(id => Bot.GetUser(id)).ToList(); }
        }

        /// <summary>
        /// Gets the Group instance (or null if not a group) for the room.
        /// </summary>
        public SteamGroup Group
        {
            get { return Bot.SteamClans.Get(Id); }
        }

        /// <summary>
        /// Toggle for echoing sent messages to the OnMessage event.
        /// </summary>
        public bool EchoSelf = false;

        /// <summary>
        /// Fired when the room was entered successfully.
        /// </summary>
        public event EnterEvent OnEnter;

        /// <summary>
        /// Fired when the bot leaves the room. Will also be called when entering a room fails.
        /// </summary>
        public event LeaveEvent OnLeave;

        /// <summary>
        /// Fired when a user sends a message in the room. Will only be fired for messages the bot sends if
        /// EchoSelf is enabled.
        /// </summary>
        public event MessageEvent OnMessage;

        /// <summary>
        /// Fired when a user joins the room.
        /// </summary>
        public event UserEnterEvent OnUserEnter;

        /// <summary>
        /// Fired when a user leaves the room. Provides the reason (and who caused it, if it was a kick/ban).
        /// </summary>
        public event UserLeaveEvent OnUserLeave;

        /// <summary>
        /// Send a message to the room.
        /// </summary>
        public void Send(string message)
        {
            if (!IsActive)
                return;

            if (Id.IsChatAccount)
                Bot.SteamFriends.SendChatRoomMessage(Id, EChatEntryType.ChatMsg, message);
            else
                Bot.SteamFriends.SendChatMessage(Id, EChatEntryType.ChatMsg, message);

            if (EchoSelf && OnMessage != null)
                OnMessage(this, new SteamUser(Bot, Bot.Id), message);
        }

        /// <summary>
        /// Leave the room. Will trigger OnLeave.
        /// </summary>
        public void Leave(SteamRoomLeaveReason reason)
        {
            if (!IsActive)
                return;

            IsActive = false;

            if (Bot.Running)
                Bot.SteamFriends.LeaveChat(Id);

            if (OnLeave != null)
                OnLeave(this, reason);
        }

        /// <summary>
        /// Kicks a user from the room.
        /// </summary>
        public void Kick(SteamID user)
        {
            if (Bot.Running)
                Bot.SteamFriends.KickChatMember(Id, user);
        }

        /// <summary>
        /// Bans a user from the room.
        /// </summary>
        public void Ban(SteamID user)
        {
            if (Bot.Running)
                Bot.SteamFriends.BanChatMember(Id, user);
        }

        /// <summary>
        /// Unban a user from the room.
        /// </summary>
        public void Unban(SteamID user)
        {
            if (Bot.Running)
                Bot.SteamFriends.UnbanChatMember(Id, user);
        }

        private readonly List<SteamID> _users = new List<SteamID>();
        private readonly Stopwatch _timeout = Stopwatch.StartNew();

        internal SteamRoom(SteamBot bot, SteamID id)
        {
            Bot = bot;
            Id = id;
            IsActive = true;
            _timeout.Start();
        }

        internal void Handle(CallbackMsg msg)
        {
            if (_timeout.Elapsed.TotalSeconds > 5)
                Leave(SteamRoomLeaveReason.JoinTimeout);

            msg.Handle<SteamClans.ChatEnterCallback>(callback =>
            {
                if (callback.ChatID != Id)
                    return;

                if (callback.EnterResponse != EChatRoomEnterResponse.Success)
                {
                    Leave(SteamRoomLeaveReason.JoinFailed);
                    return;
                }

                _timeout.Stop();
                
                if (OnEnter != null)
                    OnEnter(this);
            });

            msg.Handle<SteamFriends.ChatMsgCallback>(callback =>
            {
                if (callback.ChatMsgType != EChatEntryType.ChatMsg || callback.ChatRoomID != Id)
                    return;

                if (OnMessage != null)
                    OnMessage(this, new SteamUser(Bot, callback.ChatterID), callback.Message);
            });

            msg.Handle<SteamFriends.FriendMsgCallback>(callback =>
            {
                if (callback.EntryType != EChatEntryType.ChatMsg || callback.Sender != Id)
                    return;

                if (OnMessage != null)
                    OnMessage(this, new SteamUser(Bot, callback.Sender), callback.Message);
            });

            msg.Handle<SteamFriends.ChatMemberInfoCallback>(callback =>
            {
                if (callback.ChatRoomID != Id || callback.StateChangeInfo == null)
                    return;

                var state = callback.StateChangeInfo.StateChange;
                switch (state)
                {
                    case EChatMemberStateChange.Entered:
                        if (OnUserEnter != null)
                            OnUserEnter(this, new SteamUser(Bot, callback.StateChangeInfo.ChatterActedOn));

                        _users.Add(callback.StateChangeInfo.ChatterActedOn);
                        break;

                    case EChatMemberStateChange.Left:
                    case EChatMemberStateChange.Disconnected:
                        var leaveReason = state == EChatMemberStateChange.Left ? SteamRoomLeaveReason.Left : SteamRoomLeaveReason.Disconnected;
                        if (OnUserLeave != null)
                            OnUserLeave(this, new SteamUser(Bot, callback.StateChangeInfo.ChatterActedOn), leaveReason);

                        _users.Remove(callback.StateChangeInfo.ChatterActedOn);
                        break;

                    case EChatMemberStateChange.Kicked:
                    case EChatMemberStateChange.Banned:
                        var bootReason = state == EChatMemberStateChange.Kicked ? SteamRoomLeaveReason.Kicked : SteamRoomLeaveReason.Banned;
                        if (callback.StateChangeInfo.ChatterActedOn == Bot.Id)
                        {
                            Leave(bootReason);
                        }
                        else
                        {
                            if (OnUserLeave != null)
                                OnUserLeave(this, new SteamUser(Bot, callback.StateChangeInfo.ChatterActedOn), bootReason, new SteamUser(Bot, callback.StateChangeInfo.ChatterActedBy));
                        }

                        _users.Remove(callback.StateChangeInfo.ChatterActedOn);
                        break;
                }
            });
            
            // Steam sends PersonaStateCallbacks for every user in the room before sending ChatEnterCallback
            msg.Handle<SteamFriends.PersonaStateCallback>(callback =>
            {
                if (callback.SourceSteamID != Id || !_timeout.IsRunning)
                    return;

                _users.Add(callback.FriendID);
            });
        }
    }
}
