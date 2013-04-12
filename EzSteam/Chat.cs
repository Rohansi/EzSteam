using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using SteamKit2;

namespace EzSteam
{
    public class Chat
    {
        public enum LeaveReason
        {
            JoinFailed,
            JoinTimeout,

            Left,
            Disconnected,
            Kicked,
            Banned
        }

        public delegate void EnterEvent(Chat sender);
        public delegate void LeaveEvent(Chat sender, LeaveReason reason);
        public delegate void MessageEvent(Chat sender, SteamID messageSender, string message);
        public delegate void UserEnterEvent(Chat source, SteamID user);
        public delegate void UserLeaveEvent(Chat source, SteamID user, LeaveReason reason, SteamID sourceUser = null);

        /// <summary>
        /// Provides access to the associated Bot instance.
        /// </summary>
        public readonly Bot Bot;

        /// <summary>
        /// Gets the SteamID of the chat.
        /// </summary>
        public readonly SteamID Id;

        /// <summary>
        /// Returns true while the Chat is available for use.
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
                    return string.Join(" + ", Members.Select(id => Bot.SteamFriends.GetFriendPersonaName(id)));
                var clan = Bot.SteamClans.Get(Id);
                return clan != null ? clan.Name : "[unknown]";
            }
        }

        /// <summary>
        /// Gets a list of the users currently in the chat.
        /// </summary>
        public IEnumerable<SteamID> Members
        {
            get { return members.ToList(); }
        }

        /// <summary>
        /// Gets a Group instance (or null if not a group chat) for the chat.
        /// </summary>
        public Group Group
        {
            get { return Bot.SteamClans.Get(Id); }
        }

        /// <summary>
        /// Toggle for echoing sent messages to the OnMessage event.
        /// </summary>
        public bool EchoSelf = false;

        /// <summary>
        /// Fired when the chat was entered successfully.
        /// </summary>
        public event EnterEvent OnEnter;

        /// <summary>
        /// Fired when the bot leaves the chat. Will also be called when entering a chat fails.
        /// </summary>
        public event LeaveEvent OnLeave;

        /// <summary>
        /// Fired when a user sends a message in the chat. Will only be fired for messages the bot sends if
        /// EchoSelf is enabled.
        /// </summary>
        public event MessageEvent OnMessage;

        /// <summary>
        /// Fired when a user joins the chat.
        /// </summary>
        public event UserEnterEvent OnUserEnter;

        /// <summary>
        /// Fired when a user leaves the chat. Provides the reason (and who caused it, if it was a kick/ban).
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

            if (EchoSelf && OnMessage != null)
                OnMessage(this, Bot.PersonaId, message);
        }

        /// <summary>
        /// Leave the chat. Will trigger OnLeave.
        /// </summary>
        public void Leave(LeaveReason reason)
        {
            IsActive = false;
            Bot.SteamFriends.LeaveChat(Id);

            if (OnLeave != null)
                OnLeave(this, reason);
        }

        /// <summary>
        /// Kicks a user from the chat.
        /// </summary>
        public void Kick(SteamID user)
        {
            Bot.SteamFriends.KickChatMember(Id, user);
        }

        /// <summary>
        /// Bans a user from the chat.
        /// </summary>
        public void Ban(SteamID user)
        {
            Bot.SteamFriends.BanChatMember(Id, user);
        }

        /// <summary>
        /// Unban a user from the chat.
        /// </summary>
        public void Unban(SteamID user)
        {
            Bot.SteamFriends.UnbanChatMember(Id, user);
        }

        private readonly List<SteamID> members = new List<SteamID>();
        private readonly Stopwatch timeout = Stopwatch.StartNew();

        internal Chat(Bot bot, SteamID id)
        {
            Bot = bot;
            Id = id;
            IsActive = true;
            timeout.Start();
        }

        internal void Handle(CallbackMsg msg)
        {
            if (timeout.Elapsed.TotalSeconds > 5)
                Leave(LeaveReason.JoinTimeout);

            msg.Handle<SteamClans.ChatEnterCallback>(callback =>
            {
                if (callback.ChatID != Id)
                    return;

                if (callback.EnterResponse != EChatRoomEnterResponse.Success)
                {
                    Leave(LeaveReason.JoinFailed);
                    return;
                }

                timeout.Stop();
                
                if (OnEnter != null)
                    OnEnter(this);
            });

            msg.Handle<SteamFriends.ChatMsgCallback>(callback =>
            {
                if (callback.ChatMsgType != EChatEntryType.ChatMsg || callback.ChatRoomID != Id)
                    return;

                if (OnMessage != null)
                    OnMessage(this, callback.ChatterID, callback.Message);
            });

            msg.Handle<SteamFriends.FriendMsgCallback>(callback =>
            {
                if (callback.EntryType != EChatEntryType.ChatMsg || callback.Sender != Id)
                    return;

                if (OnMessage != null)
                    OnMessage(this, callback.Sender, callback.Message);
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
                            OnUserEnter(this, callback.StateChangeInfo.ChatterActedOn);

                        members.Add(callback.StateChangeInfo.ChatterActedOn);
                        break;

                    case EChatMemberStateChange.Left:
                    case EChatMemberStateChange.Disconnected:
                        var leaveReason = state == EChatMemberStateChange.Left ? LeaveReason.Left : LeaveReason.Disconnected;
                        if (OnUserLeave != null)
                            OnUserLeave(this, callback.StateChangeInfo.ChatterActedOn, leaveReason);

                        members.Remove(callback.StateChangeInfo.ChatterActedOn);
                        break;

                    case EChatMemberStateChange.Kicked:
                    case EChatMemberStateChange.Banned:
                        var bootReason = state == EChatMemberStateChange.Kicked ? LeaveReason.Kicked : LeaveReason.Banned;
                        if (callback.StateChangeInfo.ChatterActedOn == Bot.PersonaId)
                        {
                            Leave(bootReason);
                        }
                        else
                        {
                            if (OnUserLeave != null)
                                OnUserLeave(this, callback.StateChangeInfo.ChatterActedOn, bootReason, callback.StateChangeInfo.ChatterActedBy);
                        }

                        members.Remove(callback.StateChangeInfo.ChatterActedOn);
                        break;
                }
            });
            
            // Steam sends PersonaStateCallbacks for every user in chat before sending ChatEnterCallback
            msg.Handle<SteamFriends.PersonaStateCallback>(callback =>
            {
                if (callback.SourceSteamID != Id || !timeout.IsRunning)
                    return;
                members.Add(callback.FriendID);
            });
        }
    }
}
