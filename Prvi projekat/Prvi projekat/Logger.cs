using System;

namespace Prvi_projekat
{
    public class Logger
    {
        private static readonly object _lock = new object();
        private static readonly string _putanja = "server_log.txt";

        public static void Log(string poruka, bool iskljucivanje = false)
        {
            string logZapis = $"[ Nit {Thread.CurrentThread.ManagedThreadId} ] [{DateTime.Now : HH:mm:ss} ] {poruka}";
            
            lock(_lock)
            {
                File.AppendAllText(_putanja, logZapis + Environment.NewLine);

                if(iskljucivanje)
                {
                    File.AppendAllText(_putanja, "--------------------------------------------------------------------------------" + Environment.NewLine);
                }
            }
        }
    }
}