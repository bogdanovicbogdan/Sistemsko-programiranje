using System;
using System.Text.Json;
using System.Collections.Concurrent;
using DotNetEnv;

namespace Drugi_projekat
{
    public class Cache : IDisposable
    {
        private readonly ConcurrentDictionary<string, CacheStavka> _cache;
        private readonly LinkedList<string> _lruLista;
        private static string? _apiKey;
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly int _maxVelicina;
        private readonly TimeSpan _ttl;
        private readonly object _cacheStructureLock = new object();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new();

        private int _brojPogodaka = 0;
        private int _brojPromasaja = 0;
        private int _brojIzbacivanja = 0;
        private int _stampedoCekanja = 0;
        private int _brojCleanupIzbacivanja = 0;

        private Thread _cleanupThread;
        private bool _running = true;

        public Cache(int maxVelicina = 10, int ttlSeconds = 20)
        {
            _maxVelicina = maxVelicina;
            _ttl = TimeSpan.FromSeconds(ttlSeconds);
            _cache = new ConcurrentDictionary<string, CacheStavka>();
            _lruLista = new LinkedList<string>();

            Env.Load();
            _apiKey = Environment.GetEnvironmentVariable("API_KEY");
            if (string.IsNullOrEmpty(_apiKey))
            {
                Console.WriteLine("Greska prilikom ucitavanja API kljuca iz .env fajla");
                throw new Exception("API key nije nadjen!");
            }

            _cleanupThread = new Thread(CleanupLoop);
            _cleanupThread.IsBackground = true;
            _cleanupThread.Start();
            Logger.Log("Keš cleanup nit je pokrenuta.");
        }

        public async Task<List<Clanak>> GetAsync(string query)
        {
            string kljuc = GenerisiCacheKey(query);

            if (_cache.TryGetValue(kljuc, out CacheStavka? stavka) && !stavka.IsExpired)
            {
                lock (_cacheStructureLock)
                {
                    if (stavka.LruNode.List != null)
                    {
                        _lruLista.Remove(stavka.LruNode);
                        _lruLista.AddFirst(stavka.LruNode);
                    }
                    _brojPogodaka++;
                    Logger.Log($"Keš pogodak za ključ: {kljuc}");
                }
                return stavka.Clanci;
            }

            SemaphoreSlim keyLock = _keyLocks.GetOrAdd(kljuc, _ => new SemaphoreSlim(1, 1));
            await keyLock.WaitAsync();
            try
            {
                if (_cache.TryGetValue(kljuc, out CacheStavka? stavka1) && !stavka1.IsExpired)
                {
                    lock (_cacheStructureLock)
                    {
                        _stampedoCekanja++;
                        Logger.Log($"Keš stampedo sprečen za ključ: {kljuc}");
                        return stavka1.Clanci;
                    }
                }

                Logger.Log($"Keš promašaj za ključ: {kljuc}. Pokrećem async API poziv...");

                List<Clanak> rezultati = await FetchFromApiAsync(kljuc)
                    .ContinueWith(apiTask =>
                    {
                        if (apiTask.IsFaulted)
                        {
                            Logger.Log($"API poziv nije uspeo za ključ {kljuc}: {apiTask.Exception?.InnerException?.Message}");
                            throw apiTask.Exception!.InnerException ?? new Exception("Nepoznata greška pri API pozivu.");
                        }
                        Logger.Log($"API poziv uspešno završen za ključ: {kljuc}, " +
                                   $"dobijeno {apiTask.Result.Count} članaka.");
                        return apiTask.Result;

                    }, TaskContinuationOptions.ExecuteSynchronously);

                lock (_cacheStructureLock)
                {
                    _brojPromasaja++;

                    if (_cache.TryRemove(kljuc, out var staraStavka))
                    {
                        if (staraStavka.LruNode.List != null)
                        {
                            _lruLista.Remove(staraStavka.LruNode);
                        }
                    }

                    if (_cache.Count >= _maxVelicina && _lruLista.Last != null)
                    {
                        string kljucZaBrisanje = _lruLista.Last.Value;
                        if (_cache.TryRemove(kljucZaBrisanje, out var stariNode))
                        {
                            _lruLista.Remove(stariNode.LruNode);
                            _brojIzbacivanja++;
                            Logger.Log($"Keš izbacivanje (LRU): {kljucZaBrisanje}");

                            _keyLocks.TryRemove(kljucZaBrisanje, out _);
                        }
                    }

                    var node = new LinkedListNode<string>(kljuc);
                    _lruLista.AddFirst(node);
                    _cache[kljuc] = new CacheStavka(node, _ttl) { Clanci = rezultati };
                }

                return rezultati;
            }
            catch (Exception e)
            {
                Logger.Log($"Greška pri obradi ključa {kljuc}: {e.Message}");
                throw;
            }
            finally
            {
                keyLock.Release();
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

        private async Task<List<Clanak>> FetchFromApiAsync(string queryParametri)
        {
            var encoded = Uri.EscapeDataString(queryParametri);
            var url = $"https://api.nytimes.com/svc/search/v2/articlesearch.json?q={encoded}&api-key={_apiKey}";

            try
            {
                string jsonString = await _httpClient.GetStringAsync(url);

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
                throw;
            }
        }

        private void CleanupLoop()
        {
            while (_running)
            {
                try
                {
                    Thread.Sleep(10000);

                    lock (_cacheStructureLock)
                    {
                        var keysToRemove = new List<string>();

                        foreach (var kvp in _cache)
                        {
                            if (kvp.Value.IsExpired)
                                keysToRemove.Add(kvp.Key);
                        }

                        foreach (var key in keysToRemove)
                        {
                            if (_cache.TryRemove(key, out var stavkaZaBrisanje))
                            {
                                if (stavkaZaBrisanje.LruNode.List != null)
                                {
                                    _lruLista.Remove(stavkaZaBrisanje.LruNode);
                                }
                                _brojCleanupIzbacivanja++;
                            }

                            _keyLocks.TryRemove(key, out _);
                        }
                    }
                }
                catch (ThreadInterruptedException)
                {
                    break;
                }
                catch (Exception e)
                {
                    Logger.Log($"Cleanup thread greška: {e.Message}");
                }
            }
        }

        public string IspisiStatistiku()
        {
            lock (_cacheStructureLock)
            {
                double hitRate = (_brojPogodaka + _brojPromasaja) > 0 ? (double)_brojPogodaka / (_brojPogodaka + _brojPromasaja) * 100 : 0;

                string stats =
                    "\n================ CACHE STATISTIKE ================\n" +
                    $"Pogodaka:                 {_brojPogodaka}\n" +
                    $"Promasaja:                {_brojPromasaja}\n" +
                    $"Hit rate:                 {hitRate:F2}%\n" +
                    $"LRU izbacivanja:          {_brojIzbacivanja}\n" +
                    $"Stampede sprecavanja:     {_stampedoCekanja}\n" +
                    $"Cleanup izbacivanja:      {_brojCleanupIzbacivanja}\n" +
                    $"Trenutno u kešu:          {_cache.Count}/{_maxVelicina}\n" +
                    $"Keš TTL:                  {_ttl.TotalSeconds} seconds\n" +
                    "==================================================";

                Logger.Log(stats);

                return stats;
            }
        }

        public void Dispose()
        {
            _running = false;
            if (_cleanupThread.IsAlive)
            {
                _cleanupThread.Interrupt();
                _cleanupThread.Join(1000);
            }

            foreach (var keyLock in _keyLocks.Values)
            {
                keyLock?.Dispose();
            }
            _keyLocks.Clear();
        }
    }
}