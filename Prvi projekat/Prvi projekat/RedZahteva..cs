using System;
using System.Net;

namespace Prvi_projekat
{
    public class RedZahteva
    {
        private Queue<HttpListenerContext> _zahtevi;
        private object _lock = new object();

        public RedZahteva()
        {
            _zahtevi = new Queue<HttpListenerContext>();
        }

        public void DodajZahtev(HttpListenerContext zahtev)
        {
            lock (_lock)
            {
                _zahtevi.Enqueue(zahtev);
                Monitor.Pulse(_lock);
            }
        }

        public HttpListenerContext? UzmiZahtev(bool serverAktivan)
        {
            lock (_lock)
            {
                while(_zahtevi.Count == 0 && serverAktivan)
                {
                    Monitor.Wait(_lock);
                }

                if (_zahtevi.Count > 0)
                {
                    return _zahtevi.Dequeue();
                }
                return null;
            }
        }

        public void PrekiniSve()
        {
            lock (_lock)
            {
                Monitor.PulseAll(_lock);
            }
        }
    }
}