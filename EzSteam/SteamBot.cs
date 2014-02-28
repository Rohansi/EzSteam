using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using SteamKit2;
using SteamKit2.Internal;
using SKSteamUser = SteamKit2.SteamUser;

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
        public delegate void PrivateEnterEvent(SteamBot bot, SteamRoom room);
        public delegate void RoomInviteEvent(SteamBot bot, SteamUser sender, SteamID roomId);
        public delegate void FriendRequestEvent(SteamBot bot, SteamUser sender);

        /// <summary>
        /// Create a new SteamBot. Username and password must be for the Steam account you
        /// want it to use. Steam Guard may need to be disabled for login to work.
        /// </summary>
        public SteamBot(string username, string password)
        {
            _username = username;
            _password = password;
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
        /// Called when a new private room should be initialized.
        /// </summary>
        public event PrivateEnterEvent OnPrivateEnter;

        /// <summary>
        /// Called when somebody invites the bot to a room.
        /// </summary>
        public event RoomInviteEvent OnRoomInvite;

        /// <summary>
        /// Called when somebody adds the bot to its friend list.
        /// </summary>
        public event FriendRequestEvent OnFriendRequest;

        /// <summary>
        /// Gets the SteamID of the logged in Steam account.
        /// </summary>
        public SteamID Id
        {
            get { return SteamUser.SteamID; }
        }

        /// <summary>
        /// Gets or sets the state of the logged in user.
        /// </summary>
        public EPersonaState State
        {
            get { return SteamFriends.GetPersonaState(); }
            set
            {
                // steam removes us from chats if we switch to offline
                if (value == EPersonaState.Offline && State != EPersonaState.Offline)
                {
                    foreach (var chat in Rooms)
                    {
                        chat.Leave(SteamRoomLeaveReason.Disconnected);
                    }
                }

                SteamFriends.SetPersonaState(value);
            }
        }

        /// <summary>
        /// Gets or sets the display name of the logged in user.
        /// </summary>
        public string DisplayName
        {
            get { return SteamFriends.GetPersonaName(); }
            set { SteamFriends.SetPersonaName(value); }
        }

        /// <summary>
        /// Gets the friend list of the logged in user.
        /// </summary>
        public IEnumerable<SteamUser> Friends
        {
            get
            {
                for (var i = 0; i < SteamFriends.GetFriendCount(); i++)
                {
                    yield return GetUser(SteamFriends.GetFriendByIndex(i));
                }
            }
        }

        /// <summary>
        /// Gets the group list of the logged in user.
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
        /// Gets a list of rooms the bot is currently in.
        /// </summary>
        public List<SteamRoom> Rooms
        {
            get
            {
                lock (_rooms)
                    return _rooms.ToList();
            }
        } 

        /// <summary>
        /// Gets or sets a game the logged in user should be playing. Only supports non-Steam games.
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
            SteamUser = SteamClient.GetHandler<SKSteamUser>();
            SteamFriends = SteamClient.GetHandler<SteamFriends>();
            SteamClans = new SteamClans(this);
            SteamClient.AddHandler(SteamClans);

            SteamClient.Connect();

            _rooms = new List<SteamRoom>();

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

            foreach (var room in Rooms)
            {
                room.Leave(SteamRoomLeaveReason.Disconnected);
            }

            lock (_rooms)
                _rooms.Clear();

            if (OnDisconnected != null)
                OnDisconnected(this, reason);
        }

        /// <summary>
        /// Attempt to join a room. Will call OnLeave if the room could not be joined.
        /// </summary>
        public SteamRoom Join(SteamID roomId)
        {
            if (roomId.IsClanAccount)
            {
                SteamFriends.RequestFriendInfo(roomId, EClientPersonaStateFlag.ClanInfo | EClientPersonaStateFlag.ClanTag | EClientPersonaStateFlag.PlayerName);
                roomId = Util.ChatFromClan(roomId);
            }

            var roomsCopy = Rooms;
            if (roomsCopy.Any(r => r.Id == roomId && r.IsActive))
                return roomsCopy.Find(r => r.Id == roomId);

            var room = new SteamRoom(this, roomId);
            SteamFriends.JoinChat(roomId);

            lock (_rooms)
                _rooms.Add(room);

            return room;
        }

        /// <summary>
        /// Gets the persona of another Steam user. Will return null if the given ID is
        /// not valid. Information may not be instantly available if the user was not 
        /// "seen" before.
        /// </summary>
        public SteamUser GetUser(SteamID id)
        {
            return id.IsIndividualAccount ? new SteamUser(this, id) : null;
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
        public SteamClient SteamClient;

        /// <summary>
        /// Provides access to the internal SteamKit SteamUser instance.
        /// </summary>
        public SKSteamUser SteamUser;

        /// <summary>
        /// Provides access to the internal SteamKit SteamFriends instance.
        /// </summary>
        public SteamFriends SteamFriends;

        internal SteamClans SteamClans;
        internal bool Running;

        private readonly string _username;
        private readonly string _password;

        private List<SteamRoom> _rooms;
        private Thread _updateThread;
        private string _playing;

        private Stopwatch _timeSinceLast;

        private void Run()
        {
            _timeSinceLast = Stopwatch.StartNew();

            while (Running)
            {
                lock (_rooms)
                    _rooms.RemoveAll(c => !c.IsActive);

                var msg = SteamClient.GetCallback(true);
                if (msg == null)
                {
                    // SteamUser.SessionTokenCallback should come in every 5 minutes
                    if (_timeSinceLast.Elapsed.TotalMinutes >= 10)
                    {
                        Disconnect();
                        break;
                    }

                    Thread.Sleep(1);
                    continue;
                }

                _timeSinceLast.Restart();

                msg.Handle<SteamClient.ConnectedCallback>(callback =>
                {
                    if (callback.Result != EResult.OK)
                    {
                        Disconnect(SteamBotDisconnectReason.ConnectFailed);
                        return;
                    }

                    SteamUser.LogOn(new SKSteamUser.LogOnDetails
                    {
                        Username = _username,
                        Password = _password
                    });
                });

                msg.Handle<SteamClient.DisconnectedCallback>(callback =>
                {
                    Disconnect();
                });

                msg.Handle<SKSteamUser.LoggedOnCallback>(callback =>
                {
                    if (callback.Result == EResult.OK)
                        return;

                    var res = SteamBotDisconnectReason.LoginFailed;

                    if (callback.Result == EResult.AccountLogonDenied)
                        res = SteamBotDisconnectReason.SteamGuard;

                    if (callback.Result == EResult.InvalidPassword)
                        res = SteamBotDisconnectReason.WrongPassword;

                    Disconnect(res);
                });

                msg.Handle<SKSteamUser.LoginKeyCallback>(callback =>
                {
                    if (OnConnected != null)
                        OnConnected(this);
                });

                msg.Handle<SKSteamUser.LoggedOffCallback>(callback =>
                {
                    Disconnect(SteamBotDisconnectReason.LoggedOff);
                });

                msg.Handle<SteamFriends.FriendMsgCallback>(callback =>
                {
                    if (callback.EntryType != EChatEntryType.ChatMsg) return;

                    if (Rooms.Count(c => c.Id == callback.Sender) == 0)
                    {
                        var c = Join(callback.Sender);

                        if (OnPrivateEnter != null)
                            OnPrivateEnter(this, c);
                    }
                });

                msg.Handle<SteamFriends.FriendsListCallback>(callback =>
                {
                    foreach (var friend in callback.FriendList)
                    {
                        var f = friend;
                        if (friend.Relationship == EFriendRelationship.RequestRecipient && OnFriendRequest != null)
                            OnFriendRequest(this, new SteamUser(this, f.SteamID));
                    }
                });

                msg.Handle<SteamFriends.ChatInviteCallback>(callback =>
                {
                    if (OnRoomInvite != null)
                        OnRoomInvite(this, new SteamUser(this, callback.PatronID), callback.ChatRoomID);
                });

                foreach (var room in Rooms)
                {
                    room.Handle(msg);
                }
            }
        }
    }
}
