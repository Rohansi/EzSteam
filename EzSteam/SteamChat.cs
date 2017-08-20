using System;
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
        public SteamBot Bot { get; }

        /// <summary>
        /// Gets the SteamID of the chat.
        /// </summary>
        public SteamID Id { get; }

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
                if (!Bot.Running)
                    return "[disconnected]";
                if (Id.IsIndividualAccount)
                    return Bot.SteamFriends.GetFriendPersonaName(Id);
                if (Id.AccountInstance == 3260)
                    return string.Join(" + ", Users.Select(u => u.DisplayName));
                return Bot.SteamFriends.GetClanName(Id) ?? "[unknown]";
            }
        }

        /// <summary>
        /// Gets a list of the users currently in the chat.
        /// </summary>
        public IEnumerable<SteamPersona> Users
        {
            get
            {
                if (!Bot.Running)
                    return new SteamPersona[0];

                lock (_users)
                    return _users.Select(id => Bot.GetPersona(id)).ToList();
            }
        }

        /// <summary>
        /// Gets the Group instance (or null if not a group) for the chat.
        /// </summary>
        public SteamGroup Group { get; private set; }

        /// <summary>
        /// Toggle for echoing sent messages to the OnMessage event.
        /// </summary>
        public bool EchoSelf { get; } = false;

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
            if (!Bot.Running || !IsActive)
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

        private bool _entered;
        private readonly List<SteamID> _users = new List<SteamID>();
        private readonly Stopwatch _timeout = Stopwatch.StartNew();
        private readonly List<IDisposable> _callbackDisposables = new List<IDisposable>();

        internal SteamChat(SteamBot bot, SteamID id)
        {
            Bot = bot;
            Id = id;
            IsActive = true;
            _timeout.Start();
        }

        internal void Subscribe()
        {
            var callbackMgr = Bot.CallbackManager;

            lock (_callbackDisposables)
            {
                _callbackDisposables.Add(callbackMgr.Subscribe<SteamFriends.ChatEnterCallback>(callback =>
                {
                    if (callback.ChatID != Id || _entered)
                        return;

                    _entered = true;

                    if (callback.EnterResponse != EChatRoomEnterResponse.Success)
                    {
                        Leave(SteamChatLeaveReason.JoinFailed);
                        return;
                    }

                    _timeout.Stop();

                    if (callback.ClanID != 0)
                        Group = new SteamGroup(Bot, callback.ClanID);

                    lock (_users)
                    {
                        _users.Clear();

                        foreach (var member in callback.ChatMembers)
                        {
                            _users.Add(member.SteamID);
                            Group?.SetRank(member.SteamID, member.Details);
                        }
                    }

                    OnEnter?.Invoke(this);
                }));

                _callbackDisposables.Add(callbackMgr.Subscribe<SteamFriends.ChatMsgCallback>(callback =>
                {
                    if (callback.ChatRoomID != Id || callback.ChatMsgType != EChatEntryType.ChatMsg)
                        return;

                    OnMessage?.Invoke(this, new SteamPersona(Bot, callback.ChatterID), callback.Message);
                }));

                _callbackDisposables.Add(callbackMgr.Subscribe<SteamFriends.FriendMsgCallback>(callback =>
                {
                    if (callback.Sender != Id)
                        return;

                    switch (callback.EntryType)
                    {
                        case EChatEntryType.ChatMsg:
                            OnMessage?.Invoke(this, new SteamPersona(Bot, callback.Sender), callback.Message);
                            break;

                        case EChatEntryType.LeftConversation:
                        case EChatEntryType.Disconnected:
                            Leave(SteamChatLeaveReason.Left);
                            break;
                    }
                }));

                _callbackDisposables.Add(callbackMgr.Subscribe<SteamFriends.ChatMemberInfoCallback>(callback =>
                {
                    if (callback.ChatRoomID != Id || callback.StateChangeInfo == null)
                        return;

                    var state = callback.StateChangeInfo.StateChange;
                    switch (state)
                    {
                        case EChatMemberStateChange.Entered:
                            OnUserEnter?.Invoke(this, new SteamPersona(Bot, callback.StateChangeInfo.ChatterActedOn));

                            lock (_users)
                                _users.Add(callback.StateChangeInfo.ChatterActedOn);

                            Group?.SetRank(callback.StateChangeInfo.ChatterActedOn, callback.StateChangeInfo.MemberInfo.Details);
                            break;

                        case EChatMemberStateChange.Left:
                        case EChatMemberStateChange.Disconnected:
                            var leaveReason = state == EChatMemberStateChange.Left ? SteamChatLeaveReason.Left : SteamChatLeaveReason.Disconnected;
                            OnUserLeave?.Invoke(this, new SteamPersona(Bot, callback.StateChangeInfo.ChatterActedOn), leaveReason);

                            lock (_users)
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

                            lock (_users)
                                _users.Remove(callback.StateChangeInfo.ChatterActedOn);
                            break;
                    }
                }));
            }
        }

        internal void Unsubscribe()
        {
            lock (_callbackDisposables)
            {
                foreach (var disposable in _callbackDisposables)
                {
                    disposable.Dispose();
                }

                _callbackDisposables.Clear();
            }
        }

        internal void Update()
        {
            if (_timeout.Elapsed.TotalSeconds > 5)
                Leave(SteamChatLeaveReason.JoinTimeout);
        }
    }
}
