using System;

namespace Prvi_projekat
{
    public class Cache
    {
        private static Cache? _instance = null;
        private readonly Dictionary<string, string> _cache;

        public static Cache Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new Cache();

                return _instance;
            }
        }

        private Cache()
        {
            _cache = new Dictionary<string, string>();
        }

        public void Add(string key, string value)
        {
            lock (_cache)
            {
                _cache[key] = value;
            }
        }

        public bool TryGetValue(string key, out string value)
        {
            lock (_cache)
            {
                return _cache.TryGetValue(key, out value);
            }
        }
    }
}