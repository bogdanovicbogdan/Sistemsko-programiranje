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
        private static int ttlSekunadi = 60;
        public static int brojNiti = 10;
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

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("Pokrenuto isključivanje servera");
                Logger.Log("Isključivanje servera");
                Stop();
            };

            Task logovanjeTask = Logger.Loggovanje();

            var radnici = new Task[brojNiti];
            for (int i = 0; i < brojNiti; i++)
            {
                radnici[i] = Radnik(i);
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
                    if (!_aktivan)
                        break;

                    Console.WriteLine($"Greška u HttpListeneru: {e.Message}");
                }
                catch (Exception e)
                {
                    if (_aktivan)
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

            await logovanjeTask;
        }

        private async Task Radnik(int idRadnika)
        {
            Logger.Log($"Radnik {idRadnika} pokrenut.");

            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    HttpListenerContext? zahtev = await _redZahteva.UzmiZahtevAsync(_cts.Token);

                    if (zahtev == null)
                        break;

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        await ObradiZahtev(zahtev);
                        sw.Stop();
                        Logger.Log($"Radnik {idRadnika}: zahtev obrađen za {sw.ElapsedMilliseconds}ms");
                    }
                    catch (Exception)
                    {
                        sw.Stop();
                        Logger.Log($"Radnik {idRadnika}: zahtev završen sa greškom za {sw.ElapsedMilliseconds}ms");
                    }
                }
            }
            catch (OperationCanceledException)
            {

            }
            finally
            {
                Logger.Log($"Radnik {idRadnika} završen.");
            }
        }

        public void Stop()
        {
            _aktivan = false;
            _cts.Cancel();
            _listener.Stop();

            _redZahteva.PrekiniSve();
            _cache.Dispose();
            Logger.Log("Server je isključen.");
            Logger.PrekiniSve();
        }

        private static async Task ObradiZahtev(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            if (request.HttpMethod != "GET")
            {
                Logger.Log("Nepodrzana metoda.");
                await PosaljiOdgovor(response, 405, "Nepodrzana metoda.");
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
                await PosaljiOdgovor(response, 200, htmlStatistika);
                return;
            }

            try
            {
                string? query = request.QueryString["q"];

                if (query == null)
                {
                    await PosaljiOdgovor(response, 400, "Nedostaje query parametar 'q'.");
                    return;
                }

                Console.WriteLine($"Obrada zahteva za query: {query}");

                List<Clanak> clankovi = await _cache.GetAsync(query!);
                if (clankovi.Count == 0)
                {
                    await PosaljiOdgovor(response, 404, $"Nisu pronadjeni clanci za: {query}");
                    return;
                }

                string jsonResponse = JsonSerializer.Serialize(clankovi, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await PosaljiOdgovor(response, 200, jsonResponse, "application/json");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Greška prilikom obrade zahteva: {e.Message}");
                await PosaljiOdgovor(response, 500, $"Greska: {e.Message}");

                throw;
            }
            finally
            {
                Logger.Log("Zahtev je obrađen.");
            }
        }

        private static async Task PosaljiOdgovor(HttpListenerResponse response, int statusCode, string message, string contentType = "text/html")
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            response.ContentType = $"{contentType}; charset=utf-8";
            response.StatusCode = statusCode;
            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);

            response.Close();
        }
    }
}