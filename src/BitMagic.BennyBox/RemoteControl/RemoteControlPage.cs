using System;

namespace BitMagic.BennyBox.RemoteControl;

// Self-contained mobile control page - plain HTML/CSS/JS as a string, no build tooling, matching how
// the rest of this app avoids introducing a separate frontend toolchain for the one page that needs
// it. The token is baked in at serve time (see RemoteControlServer.HandleIndex) so every subsequent
// fetch() from the page can carry it without the user ever seeing/handling it themselves.
internal static class RemoteControlPage
{
    public static string Build(Guid token) => Html.Replace("__TOKEN__", token.ToString());

    private const string Html = """
        <!doctype html>
        <html>
        <head>
        <meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no" />
        <title>Benny Box Remote</title>
        <style>
          :root { color-scheme: dark; }
          body {
            margin: 0; padding: 24px 16px; min-height: 100vh; box-sizing: border-box;
            background: #121212; color: #eee; font-family: system-ui, -apple-system, sans-serif;
            display: flex; flex-direction: column; align-items: center; gap: 20px;
          }
          h1 { font-size: 18px; font-weight: 600; opacity: 0.8; margin: 0; }
          #title { font-size: 20px; font-weight: 600; text-align: center; min-height: 28px; }
          #position { opacity: 0.7; font-size: 14px; }
          .transport { display: flex; gap: 12px; align-items: center; justify-content: center; }
          button {
            background: #2a2a2a; color: #eee; border: 1px solid #444; border-radius: 12px;
            font-size: 28px; padding: 18px 22px; min-width: 64px; touch-action: manipulation;
          }
          button:active { background: #3a3a3a; }
          #playpause { font-size: 34px; min-width: 84px; background: #3a5a99; border-color: #4a70bb; }
          .volume-row { display: flex; align-items: center; gap: 12px; width: 100%; max-width: 340px; }
          input[type=range] { flex: 1; height: 36px; }
          #status-banner { font-size: 13px; opacity: 0.6; }
        </style>
        </head>
        <body>
          <h1>Benny Box Remote</h1>
          <div id="title">Connecting...</div>
          <div id="position"></div>
          <div class="transport">
            <button id="skipback">⏪</button>
            <button id="playpause">⏸</button>
            <button id="stop">⏹</button>
            <button id="skipforward">⏩</button>
          </div>
          <div class="volume-row">
            <button id="mute">🔊</button>
            <input id="volume" type="range" min="0" max="100" value="100" />
          </div>
          <div id="status-banner"></div>
        <script>
          const TOKEN = "__TOKEN__";

          async function api(path, opts) {
            opts = opts || {};
            opts.headers = Object.assign({ "X-Remote-Token": TOKEN, "Content-Type": "application/json" }, opts.headers);
            return fetch(path, opts);
          }

          let draggingVolume = false;

          async function refresh() {
            try {
              const res = await api("/api/status");
              if (res.status === 401) {
                document.getElementById("status-banner").textContent = "This code is no longer valid - get a new one from the app.";
                return;
              }
              if (!res.ok) return;
              const s = await res.json();
              document.getElementById("title").textContent = s.nowPlaying || "Nothing playing";
              document.getElementById("position").textContent = s.isSeekable ? (s.positionLabel + " / " + s.durationLabel) : "";
              document.getElementById("playpause").textContent = s.isPaused ? "▶" : "⏸";
              document.getElementById("playpause").style.display = s.canPause ? "" : "none";
              document.getElementById("skipback").style.display = s.isSeekable ? "" : "none";
              document.getElementById("skipforward").style.display = s.isSeekable ? "" : "none";
              document.getElementById("mute").textContent = s.isMuted ? "🔇" : "🔊";
              if (!draggingVolume) {
                document.getElementById("volume").value = s.volume;
              }
              document.getElementById("status-banner").textContent = "";
            } catch (e) {
              document.getElementById("status-banner").textContent = "Can't reach the app right now.";
            }
          }

          function send(command) {
            api("/api/command", { method: "POST", body: JSON.stringify({ command }) }).then(refresh);
          }

          document.getElementById("playpause").onclick = () => send("playpause");
          document.getElementById("stop").onclick = () => send("stop");
          document.getElementById("skipback").onclick = () => send("skipback");
          document.getElementById("skipforward").onclick = () => send("skipforward");
          document.getElementById("mute").onclick = () => send("mute");

          const volumeSlider = document.getElementById("volume");
          volumeSlider.addEventListener("pointerdown", () => draggingVolume = true);
          volumeSlider.addEventListener("pointerup", () => draggingVolume = false);
          volumeSlider.addEventListener("input", (e) => {
            api("/api/volume", { method: "POST", body: JSON.stringify({ value: parseInt(e.target.value, 10) }) });
          });

          refresh();
          setInterval(refresh, 1000);
        </script>
        </body>
        </html>
        """;
}
