using System;
using System.Threading.Channels;

namespace Drugi_projekat
{
    public class Logger
    {
        private static readonly string _putanja = "server_log.txt";
        private static readonly Channel<string>? _channel = Channel.CreateUnbounded<string>(
                new UnboundedChannelOptions { SingleWriter = false, SingleReader = true });


        public static void Log(string poruka)
        {
            string logZapis = $"[ Nit {Thread.CurrentThread.ManagedThreadId} ] [{DateTime.Now: HH:mm:ss} ] {poruka}";
            _channel!.Writer.TryWrite(logZapis);
        }

        public static async Task Loggovanje()
        {
            await foreach (var logZapis in _channel!.Reader.ReadAllAsync())
            {
                File.AppendAllText(_putanja, logZapis + Environment.NewLine);
                Console.WriteLine(logZapis);
                // await Task.Delay(100);
            }
            File.AppendAllText(_putanja, "--------------------------------------------------------------------------------" + Environment.NewLine);
        }

        public static void PrekiniSve()
        {
            _channel!.Writer.TryComplete();
        }
    }
}