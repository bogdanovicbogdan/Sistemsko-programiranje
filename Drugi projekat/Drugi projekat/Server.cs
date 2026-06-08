using System.Net;
using System.Text;
using System.Text.Json;

namespace Drugi_projekat
{
    public class Server
    {
        private static Server? _instance = null;
        private readonly HttpListener _listener;
        private readonly string _url = "http://localhost:8080/";
        private bool _aktivan = false;
        private static int velicinaKesa = 10;
        private static int ttlSekunadi = 20; // 5 minuta
        public static int brojNiti = 8;
        private static RedZahteva _redZahteva = new RedZahteva();
        private static Cache _cache = new Cache(velicinaKesa, ttlSekunadi);
        private CancellationTokenSource _cts = new CancellationTokenSource();

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

        public async Task Start()
        {
            _aktivan = true;
            _listener.Start();
            Console.WriteLine($"Server je pokrenut na {_url}");

            Logger.Log("Server je pokrenut.");

            var radnici = new Task[brojNiti];
            for (int i = 0; i < brojNiti; i++)
            {
                int idRadnika = i;
                radnici[i] = Task.Run(async() => {
                    Logger.Log($"Radnik {idRadnika} pokrenut.");

                    while (!_cts.Token.IsCancellationRequested)
                    {
                        HttpListenerContext? zahtev = await _redZahteva.UzmiZahtevAsync(_cts.Token);
                        if (zahtev == null)
                            break;

                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        await ObradiZahtev(zahtev)
                            .ContinueWith(t =>
                            {
                                sw.Stop();
                                if(t.IsFaulted)
                                    Logger.Log($"Radnik {idRadnika}: zahtev završen sa greškom za {sw.ElapsedMilliseconds}ms");
                                else
                                    Logger.Log($"Radnik {idRadnika}: zahtev obrađen za {sw.ElapsedMilliseconds}ms");
                            });
                    }

                    Logger.Log($"Radnik {idRadnika} završen.");
                }, _cts.Token);
            }

            while (_aktivan)
            {
                try
                {
                    HttpListenerContext context = await _listener.GetContextAsync();
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

            try
            {
                await Task.WhenAll(radnici.Where(t => t != null));
            }
            catch (Exception ex)
            {
                Logger.Log($"Greška radnika pri gašenju: {ex.Message}");
            }
        }

        public void Stop()
        {
            _aktivan = false;
            _cts.Cancel();
            _listener.Stop();

            _redZahteva.PrekiniSve();
            _cache.Dispose();

            Console.WriteLine("Server je isključen.");
            Logger.Log("Server je isključen.", true);
        }

        private static async Task ObradiZahtev(HttpListenerContext context)
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
                string htmlStatistika = $"<html><body><pre>{statistika}</pre></body></html>";
                PosaljiOdgovor(response, 200, htmlStatistika);
                return;
            }

            try
            {
                string? query = request.QueryString["q"];

                if (query == null)
                {
                    PosaljiOdgovor(response, 400, "Nedostaje query parametar 'q'.");
                    return;
                }

                Console.WriteLine($"Obrada zahteva za query: {query}");

                List<Clanak> clankovi = await _cache.GetAsync(query!);
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

                throw;
            }
            finally
            {
                Logger.Log("Zahtev je obrađen.");
            }
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