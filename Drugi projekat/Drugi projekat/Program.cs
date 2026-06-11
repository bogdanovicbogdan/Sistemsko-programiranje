using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Drugi_projekat
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                Server server = Server.Instance;

                // Task serverTask = Task.Run(async () => await server.Start());
                var serverTask=server.Start();

                Console.WriteLine("Pritisnite Enter za zaustavljanje servera...");

                Console.ReadLine();

                server.Stop();
                await serverTask;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Doslo je do greske: {e.Message}");
            }          
        }
    }
}