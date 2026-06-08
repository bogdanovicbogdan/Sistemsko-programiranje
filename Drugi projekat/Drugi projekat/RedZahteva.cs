using System;
using System.Net;
using System.Threading.Channels;

namespace Drugi_projekat
{
    public class RedZahteva
    {
        private readonly Channel<HttpListenerContext> _channel;

        public RedZahteva()
        {
            _channel = Channel.CreateUnbounded<HttpListenerContext>(
                new UnboundedChannelOptions{SingleWriter = true, SingleReader = false});
        }

        public void DodajZahtev(HttpListenerContext zahtev)
        {
            if(!_channel.Writer.TryWrite(zahtev))
                Logger.Log("Upozorenje: nije moguce dodati zahtev u red (kanal zatvoren).");
        }

        public async Task<HttpListenerContext?> UzmiZahtevAsync(CancellationToken ct)
        {
            try
            {
                return await _channel.Reader.ReadAsync(ct);
            }
            catch(OperationCanceledException)
            {
                return null;
            }
            catch(ChannelClosedException)
            {
                return null;
            }
        }

        public void PrekiniSve()
        {
            _channel.Writer.TryComplete();
        }
    }
}