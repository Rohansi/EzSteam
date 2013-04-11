using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EzSteam;
using Newtonsoft.Json;
using SteamKit2;

namespace Test
{
    class Program
    {
        static void Main(string[] args)
        {
            dynamic account = JsonConvert.DeserializeObject(File.ReadAllText("account.json"));
            var username = (string)account.Username;
            var password = (string)account.Password;

            var bot = new Bot(username, password);
            var chatId = new SteamID(103582791434006974); // FPP 103582791430091926 // EBFPP 103582791434006974
            
            bot.OnConnected += sender =>
            {
                Console.WriteLine("Connected");

                bot.PersonaName = "Test";
                bot.PersonaState = EPersonaState.Online;

                var chat = bot.Join(chatId);

                chat.OnEnter += steamChat =>
                {
                    Console.WriteLine("Enter");
                    chat.Send("Hello!");
                    bot.Disconnect();
                };

                chat.OnLeave += (steamChat, reason) =>
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
