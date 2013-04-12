using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SteamKit2;
using SteamKit2.Internal;

namespace EzSteam
{
    internal class SteamClans : ClientMsgHandler
    {
        public sealed class ChatEnterCallback : CallbackMsg
        {
            public SteamID ChatID { get; internal set; }
            public SteamID FriendID { get; internal set; }
            public EChatRoomType ChatRoomType { get; internal set; }
            public SteamID OwnerID { get; internal set; }
            public SteamID ClanID { get; internal set; }
            public byte ChatFlags { get; internal set; }
            public EChatRoomEnterResponse EnterResponse { get; internal set; }
        }

        private readonly Dictionary<SteamID, Group> clans = new Dictionary<SteamID, Group>(); 

        public Group Get(SteamID clanId)
        {
            if (clanId.IsChatAccount)
                clanId = Util.ClanFromChat(clanId);
            Group res;
            return clans.TryGetValue(clanId, out res) ? res : null;
        }

        internal Group GetOrAdd(SteamID clanId)
        {
            var clan = Get(clanId);
            if (clan != null)
                return clan;
            if (clanId.IsChatAccount)
                clanId = Util.ClanFromChat(clanId);
            clan = new Group(clanId);
            clans.Add(clanId, clan);
            return clan;
        }

        public override void HandleMsg(IPacketMsg packetMsg)
        {
            switch (packetMsg.MsgType)
            {
                case EMsg.ClientClanState:
                    HandleClanState(packetMsg);
                    break;

                case EMsg.ClientChatEnter:
                    HandleChatEnter(packetMsg);
                    break;

                case EMsg.ClientChatMemberInfo:
                    HandleChatMemberInfo(packetMsg);
                    break;
            }
        }

        private void HandleClanState(IPacketMsg packetMsg)
        {
            var clanMsg = new ClientMsgProtobuf<CMsgClientClanState>(packetMsg);

            if (clanMsg.Body.name_info == null)
                return; // we only care about the name

            var clan = GetOrAdd(clanMsg.Body.steamid_clan);
            clan.Name = clanMsg.Body.name_info.clan_name;
            clan.Avatar = clanMsg.Body.name_info.sha_avatar;
        }

        private void HandleChatEnter(IPacketMsg packetMsg)
        {
            var enterMsg = new ClientMsg<MsgClientChatEnter>(packetMsg);

            if (enterMsg.Body.SteamIdClan.IsValid)
            {
                var clan = GetOrAdd(enterMsg.Body.SteamIdClan);

                using (var reader = new BinaryReader(enterMsg.Payload))
                {
                    var count = reader.ReadInt32();
                    var name = ReadString(reader);
                    reader.ReadChar(); // 0

                    for (var i = 0; i < count; i++)
                    {
                        ReadString(reader); // MessageObject
                        reader.ReadByte(); // 7

                        ReadString(reader); // steamid
                        var steamId = new SteamID((ulong)reader.ReadInt64());
                        reader.ReadByte(); // 2

                        ReadString(reader); // Permissions
                        reader.ReadInt32();
                        reader.ReadByte(); // 2

                        ReadString(reader); // Details
                        var rank = (ClanRank)reader.ReadByte();
                        reader.ReadBytes(6); // who knows

                        clan.SetRank(steamId, rank);
                    }
                }
            }

            var cb = new ChatEnterCallback()
            {
                ChatID = enterMsg.Body.SteamIdChat,
                FriendID = enterMsg.Body.SteamIdFriend,
                ChatRoomType = enterMsg.Body.ChatRoomType,
                OwnerID = enterMsg.Body.SteamIdOwner,
                ClanID = enterMsg.Body.SteamIdClan,
                ChatFlags = enterMsg.Body.ChatFlags,
                EnterResponse = enterMsg.Body.EnterResponse
            };
            Client.PostCallback(cb);
        }

        private void HandleChatMemberInfo(IPacketMsg packetMsg)
        {
            var infoMsg = new ClientMsg<MsgClientChatMemberInfo>(packetMsg);

            var clan = GetOrAdd(Util.ClanFromChat(infoMsg.Body.SteamIdChat));

            using (var reader = new BinaryReader(infoMsg.Payload))
            {
                if (infoMsg.Body.Type == EChatInfoType.StateChange)
                {
                    if (infoMsg.Payload.Length == 20)
                        return; // no rank change
                    reader.ReadBytes(20);
                }

                if (infoMsg.Body.Type == EChatInfoType.MemberLimitChange)
                    return;
                
                reader.ReadByte(); // 0
                ReadString(reader); // MessageObject
                reader.ReadByte(); // 7
                ReadString(reader); // steamid
                var steamId = new SteamID((ulong)reader.ReadInt64());
                reader.ReadByte(); // 2
                ReadString(reader); // Permissions
                reader.ReadInt32();
                reader.ReadByte(); // 2
                ReadString(reader); // Details
                var rank = (ClanRank)reader.ReadByte();
                
                clan.SetRank(steamId, rank);
            }
        }

        private static string ReadString(BinaryReader reader)
        {
            var res = "";
            while (Peek(reader) != 0)
            {
                res += (char)reader.ReadByte();
            }
            reader.ReadByte(); // 0
            return res;
        }

        private static byte Peek(BinaryReader reader)
        {
            var pos = reader.BaseStream.Position;
            var res = reader.ReadByte();
            reader.BaseStream.Position = pos;
            return res;
        }
    }
}
