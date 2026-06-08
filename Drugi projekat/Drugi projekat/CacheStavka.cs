using System;

namespace Drugi_projekat
{
    public class CacheStavka
    {
        public List<Clanak> Clanci { get; set; }
        public LinkedListNode<string> LruNode { get; }
        public DateTime VremeKreiranja { get; }
        public TimeSpan Ttl { get; }
        public bool IsExpired => DateTime.UtcNow - VremeKreiranja > Ttl;

        public CacheStavka(LinkedListNode<string> node, TimeSpan ttl)
        {
            Clanci = new List<Clanak>();
            VremeKreiranja = DateTime.UtcNow;
            LruNode = node;
            Ttl = ttl;
        }

        public CacheStavka(List<Clanak> clanci, LinkedListNode<string> node, TimeSpan ttl)
        {
            Clanci = clanci;
            VremeKreiranja = DateTime.UtcNow;
            LruNode = node;
            Ttl = ttl;
        }
    }
}