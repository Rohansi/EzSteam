using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using SteamKit2;
using SteamKit2.Internal;

namespace EzSteam
{
    public enum SteamBotDisconnectReason
    {
        ConnectFailed,
        Disconnected,

        WrongPassword,
        SteamGuard,
        LoginFailed,

        LoggedOff
    }

    public sealed class SteamBot
    {
        public delegate void ConnectedEvent(SteamBot bot);
        public delegate void DisconnectedEvent(SteamBot bot, SteamBotDisconnectReason reason);
        public delegate void PrivateEnterEvent(SteamBot bot, SteamChat chat);
        public delegate void ChatInviteEvent(SteamBot bot, SteamPersona sender, SteamID chatId);
        public delegate void FriendRequestEvent(SteamBot bot, SteamPersona sender);

        /// <summary>
        /// Create a new SteamBot. Username and password must be for the Steam account you
        /// want it to use. A Steam Guard authorization code may need to be provided for
        /// login to work.
        /// </summary>
        public SteamBot(string username, string password, string authCode = null)
        {
            _username = username;
            _password = password;
            _authCode = authCode;
        }

        /// <summary>
        /// Called when the bot successfully connects and logs into Steam.
        /// </summary>
        public event ConnectedEvent OnConnected;

        /// <summary>
        /// Called when the connection to Steam is lost.
        /// </summary>
        public event DisconnectedEvent OnDisconnected;

        /// <summary>
        /// Called when a new private chat should be initialized.
        /// </summary>
        public event PrivateEnterEvent OnPrivateEnter;

        /// <summary>
        /// Called when somebody invites the bot to a chat.
        /// </summary>
        public event ChatInviteEvent OnChatInvite;

        /// <summary>
        /// Called when somebody adds the bot to its friend list.
        /// </summary>
        public event FriendRequestEvent OnFriendRequest;

        /// <summary>
        /// Gets the SteamID of the logged in Steam account.
        /// </summary>
        public SteamID SteamId => SteamUser.SteamID;

        /// <summary>
        /// Gets or sets the state of the logged in user.
        /// </summary>
        public EPersonaState PersonaState
        {
            get { return SteamFriends.GetPersonaState(); }
            set
            {
                // steam removes us from chats if we switch to offline
                if (value == EPersonaState.Offline && PersonaState != EPersonaState.Offline)
                {
                    foreach (var chat in Chats)
                    {
                        chat.Leave(SteamChatLeaveReason.Disconnected);
                    }
                }

                SteamFriends.SetPersonaState(value);
            }
        }

        /// <summary>
        /// Gets or sets the display name of the logged in persona.
        /// </summary>
        public string DisplayName
        {
            get { return SteamFriends.GetPersonaName(); }
            set { SteamFriends.SetPersonaName(value); }
        }

        /// <summary>
        /// Gets the friend list of the logged in persona.
        /// </summary>
        public IEnumerable<SteamPersona> Friends
        {
            get
            {
                for (var i = 0; i < SteamFriends.GetFriendCount(); i++)
                {
                    yield return GetPersona(SteamFriends.GetFriendByIndex(i));
                }
            }
        }

        /// <summary>
        /// Gets the group list of the logged in persona.
        /// </summary>
        public IEnumerable<SteamID> Groups
        {
            get
            {
                for (var i = 0; i < SteamFriends.GetClanCount(); i++)
                {
                    var c = SteamFriends.GetClanByIndex(i);
                    if (SteamFriends.GetClanRelationship(c) == EClanRelationship.Member)
                        yield return c;
                }
            }
        }

        /// <summary>
        /// Gets a list of chats the bot is currently in.
        /// </summary>
        public List<SteamChat> Chats
        {
            get
            {
                lock (_sync)
                    return _chats.ToList();
            }
        }

        /// <summary>
        /// Gets or sets a game the logged in persona should be playing. Only supports non-Steam games.
        /// </summary>
        public string Playing
        {
            get { return _playing; }
            set
            {
                _playing = value;

                if (string.IsNullOrEmpty(_playing))
                {
                    var msg = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayedWithDataBlob);
                    msg.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed { game_id = 0, game_extra_info = "" });
                    SteamClient.Send(msg);
                }
                else
                {
                    var msg = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayedWithDataBlob);
                    msg.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed { game_id = 9759487592989982720, game_extra_info = _playing });
                    SteamClient.Send(msg);
                }
            }
        }

        /// <summary>
        /// Connect and login to Steam.
        /// </summary>
        public void Connect()
        {
            if (SteamClient != null)
                throw new Exception("Connect was already called");

            SteamClient = new SteamClient();
            SteamUser = SteamClient.GetHandler<SteamUser>();
            SteamFriends = SteamClient.GetHandler<SteamFriends>();

            CallbackManager = new CallbackManager(SteamClient);
            InitializeCallbacks();
            
            SteamClient.Connect();
            
            lock (_sync)
                _chats = new List<SteamChat>();

            Running = true;
            _updateThread = new Thread(Run);
            _updateThread.Start();
        }

        /// <summary>
        /// Disconnect from Steam. Will call OnDisconnected.
        /// </summary>
        public void Disconnect(SteamBotDisconnectReason reason = SteamBotDisconnectReason.Disconnected)
        {
            SteamClient.Disconnect();
            Running = false;

            foreach (var chat in Chats)
            {
                chat.Leave(SteamChatLeaveReason.Disconnected);
            }

            lock (_sync)
                _chats.Clear();

            OnDisconnected?.Invoke(this, reason);
        }

        /// <summary>
        /// Attempt to join a chat. Will call OnLeave if the chat could not be joined.
        /// </summary>
        public SteamChat Join(SteamID chatId)
        {
            if (chatId.IsClanAccount)
            {
                SteamFriends.RequestFriendInfo(chatId, EClientPersonaStateFlag.ClanInfo | EClientPersonaStateFlag.ClanTag | EClientPersonaStateFlag.PlayerName);
                chatId = Util.ChatFromClan(chatId);
            }

            var chatsCopy = Chats;
            if (chatsCopy.Any(r => r.Id == chatId && r.IsActive))
                return chatsCopy.Find(r => r.Id == chatId);

            var chat = new SteamChat(this, chatId);
            chat.Subscribe();
            SteamFriends.JoinChat(chatId);

            lock (_sync)
                _chats.Add(chat);

            return chat;
        }

        /// <summary>
        /// Gets the persona from a Steam ID. Will return null if the given ID is not valid. 
        /// Information may not be instantly available if the persona was not "seen" before.
        /// </summary>
        public SteamPersona GetPersona(SteamID id)
        {
            return id.IsIndividualAccount ? new SteamPersona(this, id) : null;
        }

        /// <summary>
        /// Adds somebody to the bot's friend list.
        /// </summary>
        public void AddFriend(SteamID id)
        {
            SteamFriends.AddFriend(id);
        }

        /// <summary>
        /// Removes somebody from the bot's friend list.
        /// </summary>
        public void RemoveFriend(SteamID id)
        {
            SteamFriends.RemoveFriend(id);
        }

        /// <summary>
        /// Provides access to the internal SteamKit SteamClient instance.
        /// </summary>
        public SteamClient SteamClient { get; private set; }

        /// <summary>
        /// Provides access to the internal SteamKit CallbackManager instance.
        /// </summary>
        public CallbackManager CallbackManager { get; private set; }

        /// <summary>
        /// Provides access to the internal SteamKit SteamUser instance.
        /// </summary>
        public SteamUser SteamUser { get; private set; }

        /// <summary>
        /// Provides access to the internal SteamKit SteamFriends instance.
        /// </summary>
        public SteamFriends SteamFriends { get; private set; }

        private readonly object _sync = new object();

        internal bool Running;

        private readonly string _username;
        private readonly string _password;
        private readonly string _authCode;

        private List<SteamChat> _chats;
        private Thread _updateThread;
        private string _playing;

        private Stopwatch _timeSinceLast;

        private void InitializeCallbacks()
        {
            CallbackManager.Subscribe<SteamClient.ConnectedCallback>(callback =>
            {
                if (callback.Result != EResult.OK)
                {
                    Disconnect(SteamBotDisconnectReason.ConnectFailed);
                    return;
                }

                byte[] sentryHash = null;

                try
                {
                    if (File.Exists(GetSentryFileName()))
                    {
                        // if we have a saved sentry file, read and sha-1 hash it
                        byte[] sentryFile = File.ReadAllBytes(GetSentryFileName());
                        sentryHash = CryptoHelper.SHAHash(sentryFile);
                    }
                }
                catch (Exception)
                {
                    // just in case
                }

                SteamUser.LogOn(new SteamUser.LogOnDetails
                {
                    Username = _username,
                    Password = _password,
                    AuthCode = _authCode,
                    SentryFileHash = sentryHash
                });
            });

            CallbackManager.Subscribe<SteamUser.UpdateMachineAuthCallback>(callback =>
            {
                // confirm and save sentry file

                var sentryHash = CryptoHelper.SHAHash(callback.Data);

                File.WriteAllBytes(GetSentryFileName(), callback.Data);

                SteamUser.SendMachineAuthResponse(new SteamUser.MachineAuthDetails
                {
                    JobID = callback.JobID,

                    FileName = callback.FileName,

                    BytesWritten = callback.BytesToWrite,
                    FileSize = callback.Data.Length,
                    Offset = callback.Offset,

                    Result = EResult.OK,
                    LastError = 0,

                    OneTimePassword = callback.OneTimePassword,

                    SentryFileHash = sentryHash
                });
            });

            CallbackManager.Subscribe<SteamClient.DisconnectedCallback>(callback => Disconnect());

            CallbackManager.Subscribe<SteamUser.LoggedOnCallback>(callback =>
            {
                if (callback.Result == EResult.OK)
                {
                    OnConnected?.Invoke(this);

                    return;
                }

                var res = SteamBotDisconnectReason.LoginFailed;

                if (callback.Result == EResult.AccountLogonDenied)
                    res = SteamBotDisconnectReason.SteamGuard;

                if (callback.Result == EResult.InvalidPassword)
                    res = SteamBotDisconnectReason.WrongPassword;

                Disconnect(res);
            });

            CallbackManager.Subscribe<SteamUser.LoggedOffCallback>(callback =>
            {
                Disconnect(SteamBotDisconnectReason.LoggedOff);
            });

            CallbackManager.Subscribe<SteamFriends.FriendMsgCallback>(callback =>
            {
                if (callback.EntryType != EChatEntryType.ChatMsg) return;

                if (Chats.Count(c => c.Id == callback.Sender) == 0)
                {
                    var c = Join(callback.Sender);

                    OnPrivateEnter?.Invoke(this, c);
                }
            });

            CallbackManager.Subscribe<SteamFriends.FriendsListCallback>(callback =>
            {
                foreach (var friend in callback.FriendList)
                {
                    var f = friend;
                    if (friend.Relationship == EFriendRelationship.RequestRecipient)
                        OnFriendRequest?.Invoke(this, new SteamPersona(this, f.SteamID));
                }
            });

            CallbackManager.Subscribe<SteamFriends.ChatInviteCallback>(callback =>
            {
                OnChatInvite?.Invoke(this, new SteamPersona(this, callback.PatronID), callback.ChatRoomID);
            });
        }

        private void Run()
        {
            _timeSinceLast = Stopwatch.StartNew();

            while (Running)
            {
                lock (_sync)
                {
                    var inactiveChats = _chats.Where(c => !c.IsActive).ToList();
                    foreach (var chat in inactiveChats)
                    {
                        chat.Unsubscribe();
                        _chats.Remove(chat);
                    }
                }

                var timer = Stopwatch.StartNew();
                CallbackManager.RunWaitCallbacks(TimeSpan.FromSeconds(5));
                var elapsed = timer.Elapsed.TotalSeconds;
                var gotMessage = elapsed < 4.75;

                if (!gotMessage)
                {
                    // SteamUser.SessionTokenCallback should come in every 5 minutes
                    if (_timeSinceLast.Elapsed.TotalMinutes >= 10)
                    {
                        Disconnect();
                        break;
                    }
                }
                else
                {
                    _timeSinceLast.Restart();
                }

                foreach (var chat in Chats)
                {
                    chat.Update();
                }
            }
        }

        private string GetSentryFileName()
        {
            return $"sentry_{_username}.bin";
        }
    }
}
