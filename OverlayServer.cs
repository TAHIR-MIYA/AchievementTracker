using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AchievementTracker
{
    /// <summary>
    /// Tiny localhost web server. OBS (or any recorder with a browser source) loads
    /// http://localhost:PORT/ and every unlock is pushed to it via Server-Sent Events.
    /// Because OBS renders the page itself, the popup always ends up in the recording,
    /// whatever capture mode the game uses.
    /// </summary>
    public static class OverlayServer
    {
        private static HttpListener? listener;
        private static CancellationTokenSource? cts;
        private static readonly List<HttpListenerResponse> clients = new List<HttpListenerResponse>();
        private static readonly object gate = new object();

        public static string? LastError { get; private set; }

        public static bool IsRunning
        {
            get { var l = listener; return l != null && l.IsListening; }
        }

        public static string Url(int port) => $"http://localhost:{port}/";

        public static void Start(int port)
        {
            Stop();
            LastError = null;
            try
            {
                var l = new HttpListener();
                l.Prefixes.Add(Url(port));   // localhost only: nothing outside this PC can connect
                l.Start();
                listener = l;
                cts = new CancellationTokenSource();
                CancellationToken token = cts.Token;
                Task.Run(() => AcceptLoop(l, token));
                Task.Run(() => KeepAliveLoop(token));
            }
            catch (Exception ex)
            {
                listener = null;
                LastError = ex.Message;
            }
        }

        public static void Stop()
        {
            try { cts?.Cancel(); } catch { }
            lock (gate)
            {
                foreach (var c in clients) { try { c.Abort(); } catch { } }
                clients.Clear();
            }
            try { listener?.Close(); } catch { }
            listener = null;
            cts = null;
        }

        public static void Broadcast(string name, string iconUrl)
        {
            if (!IsRunning) return;
            string json = JsonSerializer.Serialize(new { name, icon = iconUrl });
            Send("data: " + json + "\n\n");
        }

        private static void Send(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            lock (gate)
            {
                for (int i = clients.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        clients[i].OutputStream.Write(bytes, 0, bytes.Length);
                        clients[i].OutputStream.Flush();
                    }
                    catch
                    {
                        try { clients[i].Abort(); } catch { }
                        clients.RemoveAt(i);
                    }
                }
            }
        }

        private static async Task KeepAliveLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(15000, token); } catch { break; }
                Send(": ping\n\n");
            }
        }

        private static async Task AcceptLoop(HttpListener l, CancellationToken token)
        {
            while (!token.IsCancellationRequested && l.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await l.GetContextAsync(); }
                catch { break; }
                _ = Task.Run(() => Handle(ctx));
            }
        }

        private static void Handle(HttpListenerContext ctx)
        {
            try
            {
                string path = ctx.Request.Url?.AbsolutePath ?? "/";
                HttpListenerResponse res = ctx.Response;

                if (path == "/events")
                {
                    res.ContentType = "text/event-stream";
                    res.AddHeader("Cache-Control", "no-cache");
                    res.SendChunked = true;
                    byte[] hello = Encoding.UTF8.GetBytes(": connected\n\n");
                    res.OutputStream.Write(hello, 0, hello.Length);
                    res.OutputStream.Flush();
                    lock (gate) { clients.Add(res); }
                    return;   // stay open; Broadcast() writes to it
                }

                if (path == "/unlock.mp3")
                {
                    string? file = FindAsset("unlock.mp3");
                    if (file == null) { res.StatusCode = 404; res.Close(); return; }
                    byte[] audio = File.ReadAllBytes(file);
                    res.ContentType = "audio/mpeg";
                    res.ContentLength64 = audio.Length;
                    res.OutputStream.Write(audio, 0, audio.Length);
                    res.Close();
                    return;
                }

                if (path == "/icon.ico")
                {
                    string? ico = FindAsset("app_icon.ico");
                    if (ico == null) { res.StatusCode = 404; res.Close(); return; }
                    byte[] iconBytes = File.ReadAllBytes(ico);
                    res.ContentType = "image/x-icon";
                    res.ContentLength64 = iconBytes.Length;
                    res.OutputStream.Write(iconBytes, 0, iconBytes.Length);
                    res.Close();
                    return;
                }

                if (path == "/")
                {
                    byte[] page = Encoding.UTF8.GetBytes(OverlayHtml);
                    res.ContentType = "text/html; charset=utf-8";
                    res.AddHeader("Cache-Control", "no-cache");
                    res.ContentLength64 = page.Length;
                    res.OutputStream.Write(page, 0, page.Length);
                    res.Close();
                    return;
                }

                res.StatusCode = 404;
                res.Close();
            }
            catch
            {
                try { ctx.Response.Abort(); } catch { }
            }
        }

        private static string? FindAsset(string fileName)
        {
            string a = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            if (File.Exists(a)) return a;
            string b = Path.Combine(Environment.CurrentDirectory, fileName);
            return File.Exists(b) ? b : null;
        }

        // URL options:  ?pos=top-right | top-left | bottom-right | bottom-left   &sound=1   &volume=0.0-1.0
        private const string OverlayHtml = """
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<title>Achievement Overlay</title>
<style>
  html, body { margin: 0; background: transparent; overflow: hidden;
               font-family: 'Segoe UI Variable Text', 'Segoe UI', sans-serif; }
  #stage { position: fixed; }
  .card {
    position: relative; overflow: hidden;
    display: flex; align-items: center; gap: 16px;
    box-sizing: border-box; width: 420px; height: 84px; padding: 16px;
    background: linear-gradient(180deg, #232A37, #1A1F29); color: #E8EBF1;
    border: 2px solid #2F3849; border-radius: 14px;
    box-shadow: 0 8px 28px rgba(0,0,0,.5);
    transition: transform .45s cubic-bezier(.2,.9,.3,1);
  }
  .card img { width: 52px; height: 52px; border-radius: 10px; object-fit: cover; background: #2A3242; flex: none; }
  .text { min-width: 0; }
  .small { font-size: 12px; font-weight: 600; color: #F2B544; }
  .name { font-size: 17px; font-weight: 600; margin-top: 2px; max-width: 300px;
          white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .bar { position: absolute; left: 18px; right: 18px; bottom: 7px; height: 3px; border-radius: 2px;
         background: #F2B544; transform-origin: left center;
         animation: drain 4.5s linear .45s forwards; }
  @keyframes drain { from { transform: scaleX(1); } to { transform: scaleX(0); } }
</style>
</head>
<body>
<div id="stage"></div>
<script>
  const q = new URLSearchParams(location.search);
  const pos = q.get('pos') || 'top-right';
  const withSound = q.get('sound') === '1';
  const volume = Math.min(1, Math.max(0, parseFloat(q.get('volume') || '0.5')));
  const stage = document.getElementById('stage');
  stage.style[pos.indexOf('left') >= 0 ? 'left' : 'right'] = '40px';
  stage.style[pos.indexOf('bottom') >= 0 ? 'bottom' : 'top'] = '40px';
  const hidden = pos.indexOf('bottom') >= 0 ? 'translateY(160%)' : 'translateY(-160%)';

  const queue = [];
  let busy = false;

  function enqueue(a) { queue.push(a); if (!busy) next(); }

  function next() {
    const a = queue.shift();
    if (!a) { busy = false; return; }
    busy = true;

    const card = document.createElement('div');
    card.className = 'card';
    card.style.transform = hidden;

    const img = document.createElement('img');
    img.src = (typeof a.icon === 'string' && /^https?:\/\//i.test(a.icon)) ? a.icon : '/icon.ico';
    img.onerror = function () { img.style.visibility = 'hidden'; };

    const text = document.createElement('div');
    text.className = 'text';
    const small = document.createElement('div');
    small.className = 'small';
    small.textContent = 'Achievement unlocked';
    const name = document.createElement('div');
    name.className = 'name';
    name.textContent = String(a.name || '');
    text.appendChild(small);
    text.appendChild(name);

    const bar = document.createElement('div');
    bar.className = 'bar';

    card.appendChild(img);
    card.appendChild(text);
    card.appendChild(bar);
    stage.appendChild(card);

    card.getBoundingClientRect();           // force layout so the transition runs
    card.style.transform = 'translateY(0)';
    if (withSound) {
      try { const au = new Audio('/unlock.mp3'); au.volume = volume; au.play().catch(function () {}); } catch (e) {}
    }

    setTimeout(function () { card.style.transform = hidden; }, 4950);
    setTimeout(function () { card.remove(); next(); }, 5500);
  }

  const es = new EventSource('/events');      // reconnects automatically
  es.onmessage = function (ev) { try { enqueue(JSON.parse(ev.data)); } catch (e) {} };
</script>
</body>
</html>
""";
    }
}
