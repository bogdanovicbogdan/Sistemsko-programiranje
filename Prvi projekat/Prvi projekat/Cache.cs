using System;

namespace Prvi_projekat
{
    public class Cache
    {
        private static Cache? _instance = null;
        private readonly Dictionary<string, CacheStavka> _cache;
        private static readonly object _instanceLock = new object();
        private readonly int _ttlSekundi;
        private readonly int _maxVelicina;
        private readonly LinkedList<string> _lruLista;
        private readonly object _cacheLock = new object();
        private readonly Dictionary<string, object> _keyLocks;
        private readonly object _keyLocksLock = new object();
        private int _hits = 0;
        private int _misses = 0;
        private int _evictions = 0;
        private int _stampedeWaits = 0;

        public static Cache Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null)
                            _instance = new Cache();
                    }
                }

                return _instance;
            }
        }

        private Cache(int ttlSekundi = 300, int maxVelicina = 100)
        {
            _ttlSekundi = ttlSekundi;
            _maxVelicina = maxVelicina;

            _cache = new Dictionary<string, CacheStavka>();

            _lruLista = new LinkedList<string>();

            _keyLocks = new Dictionary<string, object>();
        }

        public void Add(string key, string value)
        {
            lock (_cacheLock)
            {
                if (_cache.ContainsKey(key))
                {
                    UkloniStavku(key);
                }

                if (_cache.Count >= _maxVelicina)
                {
                    UkloniLru();
                }

                LinkedListNode<string> node = new LinkedListNode<string>(key);

                _lruLista.AddFirst(node);

                CacheStavka stavka = new CacheStavka(value, node);

                _cache[key] = stavka;

                Logger.Log($"[KES] SET -> '{key}' " + $"({_cache.Count}/{_maxVelicina})");
            }
        }

        public bool TryGetValue(string key, out string value)
        {
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(key, out CacheStavka? stavka))
                {
                    if (stavka.JeIstekla(_ttlSekundi))
                    {
                        UkloniStavku(key);

                        Logger.Log($"[KES] ISTEKLA -> '{key}'");

                        _misses++;

                        value = null;

                        return false;
                    }

                    stavka.PosledniPristup = DateTime.UtcNow;

                    _lruLista.Remove(stavka.LruNode);

                    _lruLista.AddFirst(stavka.LruNode);

                    _hits++;

                    value = stavka.Vrednost;

                    Logger.Log($"[KES] HIT -> '{key}' " + $"(preostalo: {stavka.PreostaloVreme(_ttlSekundi):F0}s)");

                    return true;
                }

                _misses++;

                value = null;

                Logger.Log($"[KES] MISS -> '{key}'");

                return false;
            }
        }

         public object GetKeyLock(string key)
        {
            lock (_keyLocksLock)
            {
                if (!_keyLocks.ContainsKey(key))
                {
                    _keyLocks[key] = new object();
                }

                return _keyLocks[key];
            }
        }

        public string GetOrAdd(string key, Func<string> fetchFunc)
        {
            if (TryGetValue(key, out string? cached))
            {
                return cached!;
            }

            object keyLock = GetKeyLock(key);

            lock (keyLock)
            {
                if (TryGetValue(key, out cached))
                {
                    _stampedeWaits++;

                    Logger.Log($"[KES] STAMPEDE PREVENCIJA -> '{key}'");

                    return cached!;
                }

                try
                {
                    Logger.Log($"[KES] API FETCH -> '{key}'");

                    string rezultat = fetchFunc();

                    Add(key, rezultat);

                    return rezultat;
                }
                catch (Exception e)
                {
                    Logger.Log($"[KES] GRESKA API FETCH -> '{key}' : {e.Message}");

                    throw;
                }
            }
        }

        private void UkloniStavku(string kljuc)
        {
            if (_cache.TryGetValue(kljuc, out CacheStavka? stavka))
            {
                _lruLista.Remove(stavka.LruNode);

                _cache.Remove(kljuc);
            }
        }

        private void UkloniLru()
        {
            if (_lruLista.Last == null)
                return;

            string lruKljuc = _lruLista.Last.Value;

            UkloniStavku(lruKljuc);

            _evictions++;

            Logger.Log($"[KES] LRU EVICTION -> '{lruKljuc}' " + $"(ukupno: {_evictions})");
        }

        private void PokreniCleanupNit()
        {
            Thread cleaner = new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(60000);

                    try
                    {
                        lock (_cacheLock)
                        {
                            List<string> istekli = _cache
                                .Where(x => x.Value.JeIstekla(_ttlSekundi))
                                .Select(x => x.Key)
                                .ToList();

                            foreach (string kljuc in istekli)
                            {
                                UkloniStavku(kljuc);

                                Logger.Log($"[KES] CLEANUP -> '{kljuc}' uklonjena");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"[KES] CLEANUP GRESKA -> {e.Message}");
                    }
                }
            });

            cleaner.IsBackground = true;

            cleaner.Start();
        }

        public void IspisiStatistike()
        {
            lock (_cacheLock)
            {
                double hitRate = (_hits + _misses) > 0 ? (double)_hits / (_hits + _misses) * 100 : 0;

                string stats =
                    "\n================ CACHE STATISTIKE ================\n" +
                    $"Hits:                 {_hits}\n" +
                    $"Misses:               {_misses}\n" +
                    $"Hit rate:             {hitRate:F2}%\n" +
                    $"LRU evictions:        {_evictions}\n" +
                    $"Stampede prevencija:  {_stampedeWaits}\n" +
                    $"Trenutno u kesu:      {_cache.Count}/{_maxVelicina}\n" +
                    "==================================================";

                Console.WriteLine(stats);

                Logger.Log(stats);
            }
        }
    }
}