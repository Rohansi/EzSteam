using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SteamKit2;

namespace EzSteam
{
    public enum SteamChatLeaveReason
    {
        JoinFailed,
        JoinTimeout,

        Left,
        Disconnected,
        Kicked,
        Banned
    }

    public sealed class SteamChat
    {
        public delegate void EnterEvent(SteamChat chat);
        public delegate void LeaveEvent(SteamChat chat, SteamChatLeaveReason reason);
        public delegate void MessageEvent(SteamChat chat, SteamPersona user, string message);
        public delegate void UserEnterEvent(SteamChat chat, SteamPersona user);
        public delegate void UserLeaveEvent(SteamChat chat, SteamPersona user, SteamChatLeaveReason reason, SteamPersona sourceUser = null);

        /// <summary>
        /// Provides access to the associated Bot instance.
        /// </summary>
        public readonly SteamBot Bot;

        /// <summary>
        /// Gets the SteamID of the chat.
        /// </summary>
        public readonly SteamID Id;

        /// <summary>
        /// Returns true while the chat is available for use.
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Gets the title (text that shows up in Steam tabs) of the chat.
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
        /// Gets a list of the users currently in the chat.
        /// </summary>
        public IEnumerable<SteamPersona> Users
        {
            get { return _users.Select(id => Bot.GetPersona(id)).ToList(); }
        }

        /// <summary>
        /// Gets the Group instance (or null if not a group) for the chat.
        /// </summary>
        public SteamGroup Group => Bot.SteamClans.Get(Id);

        /// <summary>
        /// Toggle for echoing sent messages to the OnMessage event.
        /// </summary>
        public bool EchoSelf = false;

        /// <summary>
        /// Called when the chat was entered successfully.
        /// </summary>
        public event EnterEvent OnEnter;

        /// <summary>
        /// Called when the bot leaves the chat. Will also be called when entering a chat fails.
        /// </summary>
        public event LeaveEvent OnLeave;

        /// <summary>
        /// Called when a user sends a message in the chat. Will only be called for messages the bot sends if
        /// EchoSelf is enabled.
        /// </summary>
        public event MessageEvent OnMessage;

        /// <summary>
        /// Called when a user joins the chat.
        /// </summary>
        public event UserEnterEvent OnUserEnter;

        /// <summary>
        /// Called when a user leaves the chat. Provides the reason (and who caused it, if it was a kick/ban).
        /// </summary>
        public event UserLeaveEvent OnUserLeave;

        /// <summary>
        /// Send a message to the chat.
        /// </summary>
        public void Send(string message)
        {
            if (!IsActive)
                return;

            if (Id.IsChatAccount)
                Bot.SteamFriends.SendChatRoomMessage(Id, EChatEntryType.ChatMsg, message);
            else
                Bot.SteamFriends.SendChatMessage(Id, EChatEntryType.ChatMsg, message);

            if (EchoSelf)
                OnMessage?.Invoke(this, new SteamPersona(Bot, Bot.SteamId), message);
        }

        /// <summary>
        /// Leave the chat. Will trigger OnLeave.
        /// </summary>
        public void Leave(SteamChatLeaveReason reason)
        {
            IsActive = false;

            if (Bot.Running)
                Bot.SteamFriends.LeaveChat(Id);

            OnLeave?.Invoke(this, reason);
        }

        /// <summary>
        /// Kicks a user from the chat.
        /// </summary>
        public void Kick(SteamID user)
        {
            if (Bot.Running)
                Bot.SteamFriends.KickChatMember(Id, user);
        }

        /// <summary>
        /// Bans a user from the chat.
        /// </summary>
        public void Ban(SteamID user)
        {
            if (Bot.Running)
                Bot.SteamFriends.BanChatMember(Id, user);
        }

        /// <summary>
        /// Unban a user from the chat.
        /// </summary>
        public void Unban(SteamID user)
        {
            if (Bot.Running)
                Bot.SteamFriends.UnbanChatMember(Id, user);
        }

        private readonly List<SteamID> _users = new List<SteamID>();
        private readonly Stopwatch _timeout = Stopwatch.StartNew();

        internal SteamChat(SteamBot bot, SteamID id)
        {
            Bot = bot;
            Id = id;
            IsActive = true;
            _timeout.Start();
        }

        internal void Handle(ICallbackMsg msg)
        {
            if (_timeout.Elapsed.TotalSeconds > 5)
                Leave(SteamChatLeaveReason.JoinTimeout);

            msg.Handle<SteamClans.ChatEnterCallback>(callback =>
            {
                if (callback.ChatID != Id)
                    return;

                if (callback.EnterResponse != EChatRoomEnterResponse.Success)
                {
                    Leave(SteamChatLeaveReason.JoinFailed);
                    return;
                }

                _timeout.Stop();

                OnEnter?.Invoke(this);
            });

            msg.Handle<SteamFriends.ChatMsgCallback>(callback =>
            {
                if (callback.ChatMsgType != EChatEntryType.ChatMsg || callback.ChatRoomID != Id)
                    return;

                OnMessage?.Invoke(this, new SteamPersona(Bot, callback.ChatterID), callback.Message);
            });

            msg.Handle<SteamFriends.FriendMsgCallback>(callback =>
            {
                if (callback.EntryType != EChatEntryType.ChatMsg || callback.Sender != Id)
                    return;

                OnMessage?.Invoke(this, new SteamPersona(Bot, callback.Sender), callback.Message);
            });

            msg.Handle<SteamFriends.ChatMemberInfoCallback>(callback =>
            {
                if (callback.ChatRoomID != Id || callback.StateChangeInfo == null)
                    return;

                var state = callback.StateChangeInfo.StateChange;
                switch (state)
                {
                    case EChatMemberStateChange.Entered:
                        OnUserEnter?.Invoke(this, new SteamPersona(Bot, callback.StateChangeInfo.ChatterActedOn));

                        _users.Add(callback.StateChangeInfo.ChatterActedOn);
                        break;

                    case EChatMemberStateChange.Left:
                    case EChatMemberStateChange.Disconnected:
                        var leaveReason = state == EChatMemberStateChange.Left ? SteamChatLeaveReason.Left : SteamChatLeaveReason.Disconnected;
                        OnUserLeave?.Invoke(this, new SteamPersona(Bot, callback.StateChangeInfo.ChatterActedOn), leaveReason);

                        _users.Remove(callback.StateChangeInfo.ChatterActedOn);
                        break;

                    case EChatMemberStateChange.Kicked:
                    case EChatMemberStateChange.Banned:
                        var bootReason = state == EChatMemberStateChange.Kicked ? SteamChatLeaveReason.Kicked : SteamChatLeaveReason.Banned;
                        if (callback.StateChangeInfo.ChatterActedOn == Bot.SteamId)
                        {
                            Leave(bootReason);
                        }
                        else
                        {
                            OnUserLeave?.Invoke(this, new SteamPersona(Bot, callback.StateChangeInfo.ChatterActedOn), bootReason, new SteamPersona(Bot, callback.StateChangeInfo.ChatterActedBy));
                        }

                        _users.Remove(callback.StateChangeInfo.ChatterActedOn);
                        break;
                }
            });

            // Steam sends PersonaStateCallbacks for every user in the chat before sending ChatEnterCallback
            msg.Handle<SteamFriends.PersonaStateCallback>(callback =>
            {
                if (callback.SourceSteamID != Id || !_timeout.IsRunning)
                    return;

                _users.Add(callback.FriendID);
            });
        }
    }
}
