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
            var chatId = new SteamID(103582791434244897); // FPP
            
            bot.OnConnected += sender =>
            {
                Console.WriteLine("Connected");

                bot.DisplayName = "Test";
                bot.PersonaState = EPersonaState.Online;

                var chat = bot.Join(chatId);

                chat.OnEnter += steamChat =>
                {
                    Console.WriteLine("Enter");
                    Console.WriteLine(string.Join(", ", chat.Group.Members.Select(m => m.Persona.DisplayName)));
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
