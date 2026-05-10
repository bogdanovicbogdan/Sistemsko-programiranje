using System;
using System.Text.Json;
using DotNetEnv;

namespace Prvi_projekat
{
    public class Cache
    {
        private readonly Dictionary<string, CacheStavka> _cache;
        private readonly LinkedList<string> _lruLista;
        private static string? _apiKey;
        private static readonly HttpClient _httpClient = new HttpClient();

        private readonly int _maxVelicina;
        private readonly object _lock = new object();

        private int _brojPogodaka = 0;
        private int _brojPromasaja = 0;
        private int _brojIzbacivanja = 0;
        private int _stampedoCekanja = 0;

        public Cache(int maxVelicina = 100)
        {
            _maxVelicina = maxVelicina;
            _cache = new Dictionary<string, CacheStavka>();
            _lruLista = new LinkedList<string>();

            Env.Load();
            _apiKey = Environment.GetEnvironmentVariable("API_KEY");
            if (string.IsNullOrEmpty(_apiKey))
            {
                Console.WriteLine("Greska prilikom ucitavanja API kljuca iz .env fajla");
                throw new Exception("API key nije nadjen!");
            }
        }

        public List<Clanak> Get(string query)
        {
            string kljuc = GenerisiCacheKey(query);

            while (true)
            {
                bool trebaFetch = false;

                lock (_lock)
                {
                    if (_cache.TryGetValue(kljuc, out CacheStavka? stavka))
                    {
                        if (stavka.IsLoading)
                        {
                            _stampedoCekanja++;
                            Logger.Log($"Keš stampedo čekanje na ključ: {kljuc}");

                            Monitor.Wait(_lock);

                            continue;
                        }

                        _lruLista.Remove(stavka.LruNode);
                        _lruLista.AddFirst(stavka.LruNode);

                        _brojPogodaka++;
                        Logger.Log($"Keš pogodak za ključ: {kljuc}");

                        return stavka.Clanci;
                    }

                    _brojPromasaja++;
                    Logger.Log($"Keš promašaj za ključ: {kljuc}");

                    if (_cache.Count >= _maxVelicina && _lruLista.Last != null)
                    {
                        string kljucZaBrisanje = _lruLista.Last.Value;
                        _brojIzbacivanja++;

                        if (_cache.ContainsKey(kljucZaBrisanje))
                        {
                            _lruLista.Remove(_cache[kljucZaBrisanje].LruNode);
                            _cache.Remove(kljucZaBrisanje);
                        }

                        Logger.Log($"Keš izbacivanje sa ključem: {kljucZaBrisanje}");
                    }

                    var node = new LinkedListNode<string>(kljuc);
                    _lruLista.AddFirst(node);
                    _cache[kljuc] = new CacheStavka(node);

                    trebaFetch = true;
                }

                if (trebaFetch)
                {
                    try
                    {
                        Logger.Log($"API poziv za ključ: {kljuc}");
                        List<Clanak> rezultati = FetchFromApi(kljuc);

                        lock (_lock)
                        {
                            if (_cache.TryGetValue(kljuc, out CacheStavka? placeholder))
                            {
                                placeholder.Clanci = rezultati;
                                placeholder.IsLoading = false;
                            }

                            Monitor.PulseAll(_lock);
                        }
                        return rezultati;
                    }
                    catch (Exception e)
                    {
                        lock (_lock)
                        {
                            if (_cache.ContainsKey(kljuc))
                            {
                                _lruLista.Remove(_cache[kljuc].LruNode);
                                _cache.Remove(kljuc);
                            }
                            Monitor.PulseAll(_lock);
                        }
                        Logger.Log($"API Fetch greška: {e.Message}");
                        throw;
                    }
                }
            }
        }

        private string GenerisiCacheKey(string query)
        {
            if (string.IsNullOrEmpty(query))
                return "default";

            var delovi = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            Array.Sort(delovi);

            return string.Join("&", delovi).ToLower();
        }

        private List<Clanak> FetchFromApi(string queryParametri)
        {
            var url = $"https://api.nytimes.com/svc/search/v2/articlesearch.json?q={queryParametri}&api-key={_apiKey}";

            try
            {
                var response = _httpClient.GetStringAsync(url);
                var jsonString = response.GetAwaiter().GetResult();

                using (var doc = JsonDocument.Parse(jsonString))
                {
                    if (!doc.RootElement.TryGetProperty("response", out var responseElement) || responseElement.ValueKind != JsonValueKind.Object)
                        return new List<Clanak>();

                    if (!responseElement.TryGetProperty("docs", out var docsElement) || docsElement.ValueKind != JsonValueKind.Array)
                        return new List<Clanak>();

                    var lista = new List<Clanak>();

                    foreach (var item in docsElement.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object)
                            continue;

                        string? naslov = null;
                        if (item.TryGetProperty("headline", out var headline) && headline.ValueKind == JsonValueKind.Object &&
                            headline.TryGetProperty("main", out var main) && main.ValueKind == JsonValueKind.String)
                        {
                            naslov = main.GetString();
                        }

                        string? abstractText = null;
                        if (item.TryGetProperty("abstract", out var abstractProp) && abstractProp.ValueKind == JsonValueKind.String)
                        {
                            abstractText = abstractProp.GetString();
                        }

                        string? credit = null;
                        if (item.TryGetProperty("byline", out var byline) && byline.ValueKind == JsonValueKind.Object &&
                            byline.TryGetProperty("original", out var original) && original.ValueKind == JsonValueKind.String)
                        {
                            credit = original.GetString();
                        }

                        string? webUrl = null;
                        if (item.TryGetProperty("web_url", out var webUrlProp) && webUrlProp.ValueKind == JsonValueKind.String)
                        {
                            webUrl = webUrlProp.GetString();
                        }

                        string? datumObjave = null;
                        if (item.TryGetProperty("pub_date", out var pubDate) && pubDate.ValueKind == JsonValueKind.String)
                        {
                            datumObjave = pubDate.GetString();
                        }

                        if (naslov != null && abstractText != null)
                            lista.Add(new Clanak(naslov, abstractText, credit, webUrl, datumObjave));
                    }
                    return lista;
                }
            }
            catch (Exception e)
            {
                Logger.Log($"Greška pri parsiranju API odgovora: {e.Message}");
                return new List<Clanak>();
            }
        }

        public string IspisiStatistiku()
        {
            lock (_lock)
            {
                double hitRate = (_brojPogodaka + _brojPromasaja) > 0 ? (double)_brojPogodaka / (_brojPogodaka + _brojPromasaja) * 100 : 0;

                string stats =
                    "\n================ CACHE STATISTIKE ================\n" +
                    $"Hits:                 {_brojPogodaka}\n" +
                    $"Misses:               {_brojPromasaja}\n" +
                    $"Hit rate:             {hitRate:F2}%\n" +
                    $"LRU evictions:        {_brojIzbacivanja}\n" +
                    $"Stampede prevencija:  {_stampedoCekanja}\n" +
                    $"Trenutno u kesu:      {_cache.Count}/{_maxVelicina}\n" +
                    "==================================================";

                Console.WriteLine(stats);

                Logger.Log(stats);

                return stats;
            }
        }
    }
}