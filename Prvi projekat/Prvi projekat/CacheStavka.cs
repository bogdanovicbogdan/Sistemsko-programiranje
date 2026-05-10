using System;

namespace Prvi_projekat
{
    public class CacheStavka
    {
        public List<Clanak> Clanci { get; set; }
        public DateTime PosledniPristup { get; set; }
        public LinkedListNode<string> LruNode { get; }

        public bool IsLoading { get; set; }
        public bool IsReady => !IsLoading && Clanci != null;

        public CacheStavka(LinkedListNode<string> node)
        {
            Clanci = new List<Clanak>();
            PosledniPristup = DateTime.UtcNow;
            LruNode = node;
            IsLoading = true;
        }

        public CacheStavka(List<Clanak> clanci, LinkedListNode<string> node)
        {
            Clanci = clanci;
            PosledniPristup = DateTime.UtcNow;
            LruNode = node;
            IsLoading = false;
        }
    }
}