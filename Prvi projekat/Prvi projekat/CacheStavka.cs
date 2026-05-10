using System;

namespace Prvi_projekat
{
    public class CacheStavka
    {
        public string Vrednost { get; }
        public DateTime Kreirana { get; }
        public DateTime PosledniPristup { get; set; }
        public LinkedListNode<string> LruNode { get; }

        public CacheStavka(string vrednost, LinkedListNode<string> node)
        {
            Vrednost = vrednost;
            Kreirana = DateTime.UtcNow;
            PosledniPristup = DateTime.UtcNow;
            LruNode = node;
        }

        public bool JeIstekla(int ttlSekundi)
        {
            return (DateTime.UtcNow - Kreirana).TotalSeconds > ttlSekundi;
        }

        public double PreostaloVreme(int ttlSekundi)
        {
            return ttlSekundi - (DateTime.UtcNow - Kreirana).TotalSeconds;
        }
    }
}