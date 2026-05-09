using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using DotNetEnv;

namespace Prvi_projekat
{
    public class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Server server = Server.Instance;

                Thread serverThread = new Thread(server.Start);
                serverThread.Start();

                Console.WriteLine("Pritisnite Enter za zaustavljanje servera...");
                while (Console.ReadKey().Key != ConsoleKey.Enter)
                {}

                server.Stop();
                serverThread.Join();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Doslo je do greske: {e.Message}");
            }          
        }
    }
}