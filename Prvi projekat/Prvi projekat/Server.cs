using System.Net;
using System.Text;
using System.Text.Json;

namespace Prvi_projekat
{
    public class Server
    {
        private static Server? _instance = null;
        private readonly HttpListener _listener;
        private readonly string _url = "http://localhost:8080/";
        private bool _aktivan = false;

        // Parametri:
        private static int velicinaKesa = 100;
        public static int brojNiti = 8;
        private static RedZahteva _redZahteva = new RedZahteva();
        private static Cache _cache = new Cache(velicinaKesa);

        public static Server Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new Server();

                return _instance;
            }
        }

        private Server()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(_url);
        }

        public void Start()
        {
            _aktivan = true;

            _listener.Start();
            Console.WriteLine($"Server je pokrenut na {_url}");

            Logger.Log("Server je pokrenut.");

            for (int i = 0; i < brojNiti; i++)
            {
                Thread nit = new Thread(() =>
                {
                    Logger.Log("Nit je pokrenuta.");

                    while (_aktivan)
                    {
                        HttpListenerContext? zahtev = _redZahteva.UzmiZahtev(_aktivan);
                        if (zahtev == null)
                            break;

                        ObradiZahtev(zahtev);
                    }

                    Logger.Log("Nit se završila.");
                });
                nit.IsBackground = true;
                nit.Start();
            }

            while (_aktivan)
            {
                try
                {
                    HttpListenerContext context = _listener.GetContext();
                    _redZahteva.DodajZahtev(context);
                }
                catch (HttpListenerException e)
                {
                    if(!_aktivan)
                        break;

                    Console.WriteLine($"Greška u HttpListeneru: {e.Message}");
                }
                catch (Exception e)
                {
                    if(_aktivan)
                        Console.WriteLine($"Greška kod servera: {e.Message}");
                }
            }
        }

        public void Stop()
        {
            _listener.Stop();
            _aktivan = false;

            _redZahteva.PrekiniSve();

            Console.WriteLine("Server je isključen.");
            Logger.Log("Server je isključen.", true);
        }

        private static void ObradiZahtev(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            if (request.HttpMethod != "GET")
            {
                Logger.Log("Nepodrzana metoda.");
                PosaljiOdgovor(response, 405, "Nepodrzana metoda.");
                return;
            }
            if (request.Url!.AbsolutePath == "/favicon.ico")
            {
                response.StatusCode = 404;
                response.Close();
                return;
            }

            Logger.Log($"Primljen novi zahtev: {request.Url}");

            if (request.Url.AbsolutePath == "/stats")
            {
                string statistika = _cache.IspisiStatistiku();
                PosaljiOdgovor(response, 200, statistika);
                return;
            }

            try
            {
                string? query = request.QueryString["q"];

                if (query == null) // Eksplicitna provera pre bilo kakvog rada sa kešom
                {
                    PosaljiOdgovor(response, 400, "Nedostaje query parametar 'q'.");
                    return;
                }

                Console.WriteLine($"Obrada zahteva za query: {query}");

                List<Clanak> clankovi = _cache.Get(query!);
                if (clankovi.Count == 0)
                {
                    PosaljiOdgovor(response, 404, $"Nisu pronadjeni clanci za: {query}");
                    return;
                }

                string jsonResponse = JsonSerializer.Serialize(clankovi, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                PosaljiOdgovor(response, 200, jsonResponse, "application/json");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Greška prilikom obrade zahteva: {e.Message}");
                PosaljiOdgovor(response, 500, $"Greska: {e.Message}");
            }

            Logger.Log("Zahtev je obrađen.");
        }

        private static void PosaljiOdgovor(HttpListenerResponse response, int statusCode, string message, string contentType = "text/html")
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            response.ContentType = $"{contentType}; charset=utf-8";
            response.StatusCode = statusCode;
            response.ContentLength64 = buffer.Length;
            using (var output = response.OutputStream)
            {
                output.Write(buffer, 0, buffer.Length);
            }

            response.Close();
        }
    }
}