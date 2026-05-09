using System.Net;
using System.Text;
using DotNetEnv;

namespace Prvi_projekat
{
    public class Server
    {
        private static Server? _instance = null;
        private readonly HttpListener _listener;
        private readonly string? _apiKey;
        private readonly string _url = "http://localhost:8080/";
        private bool _aktivan = false;

        public static Server Instance
        { 
            get 
            {
                if(_instance == null)
                    _instance = new Server();

                return _instance; 
            } 
        }

        private Server()
        {
            Env.Load();
            _apiKey = Environment.GetEnvironmentVariable("API_KEY");
            if (string.IsNullOrEmpty(_apiKey))
            {
                Console.WriteLine("Greska prilikom ucitavanja API kljuca iz .env fajla");
                throw new Exception("API key nije nadjen!");
            }

            _listener = new HttpListener();
            _listener.Prefixes.Add(_url);
        }

        public void Start()
        {
            _aktivan = true;

            _listener.Start();
            Console.WriteLine($"Server je pokrenut na {_url}");

            Logger.Log("Server je pokrenut.");

            while (_listener.IsListening)
            {
                try
                {
                    HttpListenerContext context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(p => ObradiZahtev(context));
                }
                catch (HttpListenerException e)
                {
                    Console.WriteLine($"Greška u HttpListeneru: {e.Message}");
                    break;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Greška: {e.Message}");
                }
            }
        }

        public void Stop()
        {
            _listener.Stop();

            Console.WriteLine("Server je isključen.");
            Logger.Log("Server je isključen.", true);
        }

        private static void ObradiZahtev(object? ctx)
        {
            Logger.Log("Primljen novi zahtev.");
            Console.WriteLine("Neki zahtev");

            Thread.Sleep(10000);
            
        }
    }
}