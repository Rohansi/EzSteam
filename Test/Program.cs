using System;
using System.IO;
using System.Linq;
using EzSteam;
using Newtonsoft.Json;
using SteamKit2;

namespace Test
{
    class Program
    {
        static void Main()
        {
            dynamic account = JsonConvert.DeserializeObject(File.ReadAllText("account.json"));
            var username = (string)account.Username;
            var password = (string)account.Password;

            var bot = new SteamBot(username, password);
            var roomId = new SteamID(103582791434244897); // FPP
            
            bot.OnConnected += sender =>
            {
                Console.WriteLine("Connected");

                bot.DisplayName = "Test";
                bot.State = EPersonaState.Online;

                var room = bot.Join(roomId);

                room.OnEnter += steamChat =>
                {
                    Console.WriteLine("Enter");
                    Console.WriteLine(string.Join(", ", room.Group.Members.Select(m => m.User.DisplayName)));
                    room.Send("Hello!");
                    bot.Disconnect();
                };

                room.OnLeave += (steamChat, reason) =>
                    Console.WriteLine("Leave " + reason);
            };

            bot.OnDisconnected += (sender, reason) =>
                Console.WriteLine("Disconnected " + reason);
            
            bot.Connect();

            while (true)
            {
                System.Threading.Thread.Sleep(1);
            }
        }
    }
}
