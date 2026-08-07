using System;

namespace BitMagic.BennyBox.RemoteControl;

// Self-contained mobile control page - plain HTML/CSS/JS as a string, no build tooling, matching how
// the rest of this app avoids introducing a separate frontend toolchain for the one page that needs
// it. The token is baked in at serve time (see RemoteControlServer.HandleIndex) so every subsequent
// fetch() from the page can carry it without the user ever seeing/handling it themselves.
//
// A small multi-tab app: Now Playing (the original transport controls) plus Live TV/Guide/Series/
// Movies/Favorites browsing tabs, each backed by the matching RemoteControlServer.*.cs route file.
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
          * { box-sizing: border-box; }
          body {
            margin: 0; padding: 0; min-height: 100vh;
            background: #121212; color: #eee; font-family: system-ui, -apple-system, sans-serif;
            display: flex; flex-direction: column;
          }
          .page { flex: 1; overflow-y: auto; padding: 16px; padding-bottom: 84px; display: none; }
          .page.active { display: block; }

          h1 { font-size: 18px; font-weight: 600; opacity: 0.8; margin: 0 0 12px; text-align: center; }

          /* Now Playing */
          #title { font-size: 20px; font-weight: 600; text-align: center; min-height: 28px; margin-top: 12px; }
          #position { opacity: 0.7; font-size: 14px; text-align: center; }
          .transport { display: flex; gap: 12px; align-items: center; justify-content: center; margin: 20px 0; }
          .transport button {
            background: #2a2a2a; color: #eee; border: 1px solid #444; border-radius: 12px;
            font-size: 28px; padding: 18px 22px; min-width: 64px; touch-action: manipulation;
          }
          .transport button:active { background: #3a3a3a; }
          #playpause { font-size: 34px !important; min-width: 84px; background: #3a5a99 !important; border-color: #4a70bb !important; }
          .volume-row { display: flex; align-items: center; gap: 12px; width: 100%; max-width: 340px; margin: 0 auto; }
          input[type=range] { flex: 1; height: 36px; }
          #status-banner { font-size: 13px; opacity: 0.6; text-align: center; margin-top: 12px; min-height: 16px; }
          #status-banner.error { color: #ff8a8a; opacity: 1; font-weight: 500; }
          .transport button:disabled, #mute:disabled, input[type=range]:disabled { opacity: 0.3; pointer-events: none; }
          button:not(:disabled) { cursor: pointer; }
          .tap { cursor: pointer; }

          /* Browsing */
          .search-row { display: flex; gap: 8px; margin-bottom: 10px; }
          input[type=text], select {
            background: #1e1e1e; color: #eee; border: 1px solid #444; border-radius: 8px;
            padding: 10px; font-size: 15px; flex: 1;
          }
          .truncated-note { font-size: 12px; opacity: 0.6; margin-bottom: 8px; }
          .list-item {
            display: flex; align-items: center; gap: 10px; padding: 10px 6px;
            border-bottom: 1px solid #262626; touch-action: manipulation;
          }
          .list-item img { width: 40px; height: 40px; object-fit: contain; border-radius: 4px; background: #1e1e1e; flex-shrink: 0; }
          .list-item .cover { width: 40px; height: 60px; object-fit: cover; border-radius: 4px; background: #1e1e1e; flex-shrink: 0; }
          .list-item .info { flex: 1; min-width: 0; }
          .list-item .name { font-size: 15px; }
          .list-item .sub { font-size: 12px; opacity: 0.65; }
          .list-item .watched { opacity: 0.5; }
          .star-btn { background: none; border: none; font-size: 20px; padding: 4px 8px; flex-shrink: 0; }
          .empty-note { opacity: 0.6; padding: 20px; text-align: center; }

          /* Detail views */
          .back-btn { background: #2a2a2a; border: 1px solid #444; color: #eee; border-radius: 8px; padding: 8px 14px; margin-bottom: 12px; }
          .detail-header { display: flex; gap: 12px; margin-bottom: 12px; }
          .detail-header .cover { width: 100px; height: 150px; object-fit: cover; border-radius: 6px; background: #1e1e1e; flex-shrink: 0; }
          .detail-header .name { font-size: 18px; font-weight: 600; }
          .detail-header .meta { font-size: 12px; opacity: 0.7; margin-top: 4px; }
          .detail-header .plot { font-size: 13px; opacity: 0.85; margin-top: 8px; }
          .play-btn { width: 100%; background: #3a5a99; border: 1px solid #4a70bb; color: #eee; border-radius: 8px; padding: 12px; font-size: 15px; margin-top: 10px; }
          .season-header { font-weight: 600; margin: 14px 0 4px; opacity: 0.8; }

          /* Progress bar (Continue Watching) */
          .progress-bar { height: 4px; background: #333; border-radius: 2px; margin-top: 4px; overflow: hidden; }
          .progress-bar .fill { height: 100%; background: #4a70bb; }
          .section-header { font-weight: 600; margin: 14px 0 4px; opacity: 0.8; font-size: 14px; }

          /* Tab bar */
          nav.tabs {
            position: fixed; bottom: 0; left: 0; right: 0; display: flex;
            background: #1a1a1a; border-top: 1px solid #333; padding: 6px 0 max(6px, env(safe-area-inset-bottom));
          }
          nav.tabs button {
            flex: 1; background: none; border: none; color: #888; font-size: 11px;
            display: flex; flex-direction: column; align-items: center; gap: 2px; padding: 6px 2px;
          }
          nav.tabs button .icon { font-size: 20px; }
          nav.tabs button.active { color: #eee; }

          /* Toast */
          #toast {
            position: fixed; left: 50%; bottom: 76px; transform: translateX(-50%) translateY(20px);
            background: #3a1f1f; color: #ffb4b4; border: 1px solid #6b2c2c; border-radius: 8px;
            padding: 10px 16px; font-size: 13px; opacity: 0; transition: opacity 0.2s, transform 0.2s;
            pointer-events: none; max-width: 90%; text-align: center; z-index: 10;
          }
          #toast.show { opacity: 1; transform: translateX(-50%) translateY(0); }
        </style>
        </head>
        <body>

          <div class="page active" id="page-now-playing">
            <h1>Benny Box Remote</h1>
            <div id="title">Connecting...</div>
            <div id="position"></div>
            <div class="transport">
              <button id="skipback" disabled>⏪</button>
              <button id="playpause" disabled>⏸</button>
              <button id="stop" disabled>⏹</button>
              <button id="skipforward" disabled>⏩</button>
            </div>
            <div class="volume-row">
              <button id="mute" disabled>🔊</button>
              <input id="volume" type="range" min="0" max="100" value="100" disabled />
            </div>
            <div id="status-banner"></div>
          </div>

          <div class="page" id="page-search">
            <h1>Search</h1>
            <div class="search-row">
              <input type="text" id="search-query" placeholder="Search Live TV, Series, and Movies..." />
            </div>
            <div id="search-results"><div class="empty-note">Type to search across Live TV, Series, and Movies.</div></div>
          </div>

          <div class="page" id="page-livetv">
            <h1>Live TV</h1>
            <div class="search-row">
              <input type="text" id="livetv-search" placeholder="Search channels..." />
              <select id="livetv-category"><option value="">All Categories</option></select>
            </div>
            <div id="livetv-list"></div>
          </div>

          <div class="page" id="page-guide">
            <h1>Guide</h1>
            <div class="search-row">
              <input type="text" id="guide-search" placeholder="Search..." />
              <select id="guide-category"><option value="">All Categories</option></select>
            </div>
            <div id="guide-list"></div>
          </div>

          <div class="page" id="page-series">
            <div id="series-list-view">
              <h1>Series</h1>
              <div class="search-row">
                <input type="text" id="series-search" placeholder="Search series..." />
                <select id="series-category"><option value="">All Categories</option></select>
              </div>
              <div id="series-list"></div>
            </div>
            <div id="series-detail-view" style="display:none"></div>
          </div>

          <div class="page" id="page-movies">
            <div id="movies-list-view">
              <h1>Movies</h1>
              <div class="search-row">
                <input type="text" id="movies-search" placeholder="Search movies..." />
                <select id="movies-category"><option value="">All Categories</option></select>
              </div>
              <div id="movies-list"></div>
            </div>
            <div id="movies-detail-view" style="display:none"></div>
          </div>

          <div class="page" id="page-favorites">
            <h1>Favorites</h1>
            <div id="favorites-list"></div>
          </div>

          <nav class="tabs">
            <button data-tab="now-playing" class="active"><span class="icon">▶️</span>Playing</button>
            <button data-tab="search"><span class="icon">🔍</span>Search</button>
            <button data-tab="livetv"><span class="icon">📺</span>Live TV</button>
            <button data-tab="guide"><span class="icon">📅</span>Guide</button>
            <button data-tab="series"><span class="icon">📋</span>Series</button>
            <button data-tab="movies"><span class="icon">🎬</span>Movies</button>
            <button data-tab="favorites"><span class="icon">⭐</span>Favs</button>
          </nav>

          <div id="toast"></div>

        <script>
          const TOKEN = "__TOKEN__";

          async function api(path, opts) {
            opts = opts || {};
            opts.headers = Object.assign({ "X-Remote-Token": TOKEN, "Content-Type": "application/json" }, opts.headers);
            return fetch(path, opts);
          }

          function esc(s) {
            const d = document.createElement("div");
            d.textContent = s == null ? "" : String(s);
            return d.innerHTML;
          }

          function debounce(fn, ms) {
            let t;
            return (...args) => { clearTimeout(t); t = setTimeout(() => fn(...args), ms); };
          }

          // ---------- Toast (feedback for actions that fail silently otherwise) ----------
          let toastTimer;
          function showToast(message) {
            const toast = document.getElementById("toast");
            toast.textContent = message;
            toast.classList.add("show");
            clearTimeout(toastTimer);
            toastTimer = setTimeout(() => toast.classList.remove("show"), 2500);
          }

          // Wraps api() for actions (play/toggle/etc) - returns true on success, shows a toast and
          // returns false otherwise, so callers can skip follow-up UI updates (switching tabs, flipping
          // an icon) when the action didn't actually happen.
          async function apiAction(path, opts, failureMessage) {
            try {
              const res = await api(path, opts);
              if (res.ok) return true;
              showToast(failureMessage || "Action failed.");
              return false;
            } catch (e) {
              showToast("Can't reach the app right now.");
              return false;
            }
          }

          // ---------- Tabs ----------
          const tabButtons = document.querySelectorAll("nav.tabs button");
          const loaders = {
            livetv: () => loadLiveTv(),
            guide: () => loadGuide(),
            series: () => { if (!seriesLoaded) loadSeriesList(); },
            movies: () => { if (!moviesLoaded) loadMoviesList(); },
            favorites: () => loadFavorites()
          };
          function showTab(tab) {
            document.querySelectorAll(".page").forEach(p => p.classList.remove("active"));
            document.getElementById("page-" + tab).classList.add("active");
            tabButtons.forEach(b => b.classList.toggle("active", b.dataset.tab === tab));
            if (loaders[tab]) loaders[tab]();
          }
          tabButtons.forEach(b => b.onclick = () => showTab(b.dataset.tab));

          // ---------- Now Playing ----------
          let draggingVolume = false;
          let hasConnectedOnce = false;

          function setTransportEnabled(enabled) {
            ["playpause", "stop", "skipback", "skipforward", "mute", "volume"].forEach(id => {
              document.getElementById(id).disabled = !enabled;
            });
          }

          function setBanner(text, isError) {
            const banner = document.getElementById("status-banner");
            banner.textContent = text;
            banner.classList.toggle("error", !!isError);
          }

          async function refreshStatus() {
            try {
              const res = await api("/api/status");
              if (res.status === 401) {
                setTransportEnabled(false);
                setBanner("This code is no longer valid - get a new one from the app.", true);
                return;
              }
              if (!res.ok) throw new Error("status " + res.status);
              const s = await res.json();
              hasConnectedOnce = true;
              document.getElementById("title").textContent = s.nowPlaying || "Nothing playing";
              document.getElementById("position").textContent = s.isSeekable ? (s.positionLabel + " / " + s.durationLabel) : "";
              setTransportEnabled(true);
              document.getElementById("playpause").textContent = s.isPaused ? "▶" : "⏸";
              document.getElementById("playpause").style.display = s.canPause ? "" : "none";
              document.getElementById("skipback").style.display = s.isSeekable ? "" : "none";
              document.getElementById("skipforward").style.display = s.isSeekable ? "" : "none";
              document.getElementById("mute").textContent = s.isMuted ? "🔇" : "🔊";
              if (!draggingVolume) document.getElementById("volume").value = s.volume;
              setBanner("", false);
            } catch (e) {
              setTransportEnabled(false);
              setBanner(hasConnectedOnce ? "Lost connection - retrying..." : "Connecting - make sure your phone is on the same network as the app...", true);
            }
          }

          function sendCommand(command) {
            api("/api/command", { method: "POST", body: JSON.stringify({ command }) }).then(refreshStatus);
          }

          document.getElementById("playpause").onclick = () => sendCommand("playpause");
          document.getElementById("stop").onclick = () => sendCommand("stop");
          document.getElementById("skipback").onclick = () => sendCommand("skipback");
          document.getElementById("skipforward").onclick = () => sendCommand("skipforward");
          document.getElementById("mute").onclick = () => sendCommand("mute");

          const volumeSlider = document.getElementById("volume");
          volumeSlider.addEventListener("pointerdown", () => draggingVolume = true);
          volumeSlider.addEventListener("pointerup", () => draggingVolume = false);
          volumeSlider.addEventListener("input", (e) => {
            api("/api/volume", { method: "POST", body: JSON.stringify({ value: parseInt(e.target.value, 10) }) });
          });

          function goToNowPlaying() { showTab("now-playing"); refreshStatus(); }

          refreshStatus();
          setInterval(refreshStatus, 1000);

          // ---------- Shared favorite star ----------
          function starButton(type, id, isFavorite, onToggled) {
            const btn = document.createElement("button");
            btn.className = "star-btn";
            btn.textContent = isFavorite ? "★" : "☆";
            btn.onclick = async (e) => {
              e.stopPropagation();
              const next = !isFavorite;
              if (await apiAction("/api/favorite", { method: "POST", body: JSON.stringify({ type, id, favorite: next }) }, "Couldn't update favorite.")) {
                isFavorite = next;
                if (onToggled) onToggled(next); else btn.textContent = next ? "★" : "☆";
              }
            };
            return btn;
          }

          function populateCategorySelect(select, categories) {
            const current = select.value;
            select.innerHTML = '<option value="">All Categories</option>' +
              categories.map(c => '<option value="' + esc(c) + '">' + esc(c) + '</option>').join("");
            select.value = categories.includes(current) ? current : "";
          }

          // ---------- Search ----------
          const searchQueryInput = document.getElementById("search-query");

          async function loadSearch() {
            const query = searchQueryInput.value.trim();
            const results = document.getElementById("search-results");
            if (!query) {
              results.innerHTML = '<div class="empty-note">Type to search across Live TV, Series, and Movies.</div>';
              return;
            }
            try {
              const res = await api("/api/search?q=" + encodeURIComponent(query));
              if (!res.ok) throw new Error("search " + res.status);
              renderSearchResults(await res.json());
            } catch (e) {
              showToast("Couldn't search.");
            }
          }

          // Every section is appended with insertAdjacentHTML, never innerHTML += - see the Favorites
          // tab fix for why mixing the two silently strips click handlers off rows added earlier.
          function renderSearchResults(data) {
            const results = document.getElementById("search-results");
            results.innerHTML = "";

            if (data.channels.length === 0 && data.series.length === 0 && data.movies.length === 0) {
              results.innerHTML = '<div class="empty-note">No matches found.</div>';
              return;
            }

            if (data.channels.length > 0) {
              results.insertAdjacentHTML("beforeend", '<div class="section-header">Live TV</div>');
              if (data.channelsTruncated) {
                results.insertAdjacentHTML("beforeend", '<div class="truncated-note">Showing first ' + data.channels.length + ' results - refine your search for more.</div>');
              }
              for (const item of data.channels) {
                const row = document.createElement("div");
                row.className = "list-item tap";
                row.innerHTML =
                  '<img src="' + esc(item.logoUrl || "") + '" onerror="this.style.visibility=\'hidden\'" />' +
                  '<div class="info"><div class="name">' + esc(item.name) + '</div></div>';
                row.onclick = () => playChannel(item.id, "/api/livetv/play");
                row.appendChild(starButton("channel", item.id, item.isFavorite));
                results.appendChild(row);
              }
            }

            if (data.series.length > 0) {
              results.insertAdjacentHTML("beforeend", '<div class="section-header">Series</div>');
              if (data.seriesTruncated) {
                results.insertAdjacentHTML("beforeend", '<div class="truncated-note">Showing first ' + data.series.length + ' results - refine your search for more.</div>');
              }
              for (const item of data.series) {
                const row = document.createElement("div");
                row.className = "list-item tap" + (item.isWatched ? " watched" : "");
                row.innerHTML =
                  '<img class="cover" src="' + esc(item.coverUrl || "") + '" onerror="this.style.visibility=\'hidden\'" />' +
                  '<div class="info"><div class="name">' + esc(item.name) + '</div></div>';
                row.onclick = () => openSeriesDetail(item.id);
                row.appendChild(starButton("series", item.id, item.isFavorite));
                results.appendChild(row);
              }
            }

            if (data.movies.length > 0) {
              results.insertAdjacentHTML("beforeend", '<div class="section-header">Movies</div>');
              if (data.moviesTruncated) {
                results.insertAdjacentHTML("beforeend", '<div class="truncated-note">Showing first ' + data.movies.length + ' results - refine your search for more.</div>');
              }
              for (const item of data.movies) {
                const row = document.createElement("div");
                row.className = "list-item tap" + (item.isWatched ? " watched" : "");
                row.innerHTML =
                  '<img class="cover" src="' + esc(item.coverUrl || "") + '" onerror="this.style.visibility=\'hidden\'" />' +
                  '<div class="info"><div class="name">' + esc(item.name) + '</div></div>';
                row.onclick = () => openMovieDetail(item.id);
                row.appendChild(starButton("movie", item.id, item.isFavorite));
                results.appendChild(row);
              }
            }
          }

          searchQueryInput.addEventListener("input", debounce(loadSearch, 300));

          // ---------- Live TV ----------
          const liveTvSearch = document.getElementById("livetv-search");
          const liveTvCategory = document.getElementById("livetv-category");

          async function loadLiveTv() {
            const params = new URLSearchParams({ search: liveTvSearch.value, category: liveTvCategory.value });
            try {
              const res = await api("/api/livetv?" + params);
              if (!res.ok) throw new Error("livetv " + res.status);
              const data = await res.json();
              populateCategorySelect(liveTvCategory, data.categories);
              renderLiveTvList(data);
            } catch (e) {
              showToast("Couldn't load Live TV.");
            }
          }

          function renderLiveTvList(data) {
            const list = document.getElementById("livetv-list");
            list.innerHTML = "";
            if (data.truncated) {
              const note = document.createElement("div");
              note.className = "truncated-note";
              note.textContent = "Showing first 200 results - refine your search for more.";
              list.appendChild(note);
            }
            if (data.items.length === 0) {
              list.innerHTML += '<div class="empty-note">No channels found.</div>';
              return;
            }
            for (const item of data.items) {
              const row = document.createElement("div");
              row.className = "list-item tap";
              row.innerHTML =
                '<img src="' + esc(item.logoUrl || "") + '" onerror="this.style.visibility=\'hidden\'" />' +
                '<div class="info"><div class="name">' + esc(item.name) + '</div>' +
                (item.nowTitle ? '<div class="sub">' + esc(item.nowTitle) + '</div>' : '') + '</div>';
              row.onclick = () => playChannel(item.id, "/api/livetv/play");
              row.appendChild(starButton("channel", item.id, item.isFavorite));
              list.appendChild(row);
            }
          }

          async function playChannel(id, path) {
            if (await apiAction(path, { method: "POST", body: JSON.stringify({ id }) }, "Couldn't play that channel.")) {
              goToNowPlaying();
            }
          }

          liveTvSearch.addEventListener("input", debounce(loadLiveTv, 300));
          liveTvCategory.addEventListener("change", loadLiveTv);

          // ---------- Guide ----------
          const guideSearch = document.getElementById("guide-search");
          const guideCategory = document.getElementById("guide-category");

          async function loadGuide() {
            const params = new URLSearchParams({ search: guideSearch.value, category: guideCategory.value });
            try {
              const res = await api("/api/guide?" + params);
              if (!res.ok) throw new Error("guide " + res.status);
              const data = await res.json();
              populateCategorySelect(guideCategory, data.categories);
              renderGuideList(data);
            } catch (e) {
              showToast("Couldn't load the guide.");
            }
          }

          function timeLabel(iso) {
            if (!iso) return "";
            const d = new Date(iso);
            return d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
          }

          function renderGuideList(data) {
            const list = document.getElementById("guide-list");
            list.innerHTML = "";
            if (data.truncated) {
              const note = document.createElement("div");
              note.className = "truncated-note";
              note.textContent = "Showing first 200 results - refine your search for more.";
              list.appendChild(note);
            }
            if (data.items.length === 0) {
              list.innerHTML += '<div class="empty-note">No channels found.</div>';
              return;
            }
            for (const item of data.items) {
              const row = document.createElement("div");
              row.className = "list-item";
              const nowPart = item.nowTitle
                ? esc(item.nowTitle) + " (" + timeLabel(item.nowStart) + "-" + timeLabel(item.nowEnd) + ")"
                : "No info";
              const nextPart = item.nextTitle
                ? "Next: " + esc(item.nextTitle) + " " + timeLabel(item.nextStart)
                : "";
              row.innerHTML =
                '<img src="' + esc(item.logoUrl || "") + '" onerror="this.style.visibility=\'hidden\'" />' +
                '<div class="info"><div class="name tap">' + esc(item.name) + '</div>' +
                '<div class="sub">' + nowPart + '</div>' +
                (nextPart ? '<div class="sub tap">' + (item.hasReminder ? "🔔 " : "🔕 ") + nextPart + '</div>' : '') +
                '</div>';
              row.querySelector(".name").onclick = () => playChannel(item.id, "/api/guide/tune");
              const nextEl = row.querySelectorAll(".sub")[1];
              if (nextEl) {
                nextEl.onclick = async () => {
                  const ok = await apiAction("/api/guide/reminder", { method: "POST", body: JSON.stringify({
                    profileId: item.profileId, channelTvgId: item.tvgId, channelName: item.name,
                    programmeTitle: item.nextTitle, startUtc: item.nextStart, endUtc: item.nextEnd, set: !item.hasReminder
                  })}, "Couldn't update reminder.");
                  if (ok) loadGuide();
                };
              }
              row.appendChild(starButton("channel", item.id, item.isFavorite));
              list.appendChild(row);
            }
          }

          guideSearch.addEventListener("input", debounce(loadGuide, 300));
          guideCategory.addEventListener("change", loadGuide);

          // ---------- Series ----------
          let seriesLoaded = false;
          const seriesSearch = document.getElementById("series-search");
          const seriesCategory = document.getElementById("series-category");

          async function loadSeriesList() {
            const params = new URLSearchParams({ search: seriesSearch.value, category: seriesCategory.value });
            let data;
            try {
              const res = await api("/api/series?" + params);
              if (!res.ok) throw new Error("series " + res.status);
              data = await res.json();
            } catch (e) {
              showToast("Couldn't load series.");
              return;
            }
            seriesLoaded = true;
            populateCategorySelect(seriesCategory, data.categories);
            const list = document.getElementById("series-list");
            list.innerHTML = "";
            if (data.truncated) {
              const note = document.createElement("div");
              note.className = "truncated-note";
              note.textContent = "Showing first 200 results - refine your search for more.";
              list.appendChild(note);
            }
            if (data.items.length === 0) {
              list.innerHTML += '<div class="empty-note">No series found.</div>';
              return;
            }
            for (const item of data.items) {
              const row = document.createElement("div");
              row.className = "list-item tap" + (item.isWatched ? " watched" : "");
              row.innerHTML =
                '<img class="cover" src="' + esc(item.coverUrl || "") + '" onerror="this.style.visibility=\'hidden\'" />' +
                '<div class="info"><div class="name">' + esc(item.name) + '</div></div>';
              row.onclick = () => openSeriesDetail(item.id);
              row.appendChild(starButton("series", item.id, item.isFavorite));
              list.appendChild(row);
            }
          }

          async function openSeriesDetail(id) {
            showTab("series");
            document.getElementById("series-list-view").style.display = "none";
            const view = document.getElementById("series-detail-view");
            view.style.display = "";
            view.innerHTML = '<button class="back-btn">&larr; Back to Series</button><div class="empty-note">Loading...</div>';
            view.querySelector(".back-btn").onclick = closeSeriesDetail;

            let s;
            try {
              const res = await api("/api/series/" + id);
              if (!res.ok) throw new Error("series detail " + res.status);
              s = await res.json();
            } catch (e) {
              view.innerHTML = '<button class="back-btn">&larr; Back to Series</button><div class="empty-note">Failed to load.</div>';
              view.querySelector(".back-btn").onclick = closeSeriesDetail;
              return;
            }

            let html = '<button class="back-btn">&larr; Back to Series</button>';
            html += '<div class="detail-header"><img class="cover" src="' + esc(s.coverUrl || "") + '" onerror="this.style.visibility=\'hidden\'" />';
            html += '<div><div class="name">' + esc(s.name) + '</div>';
            if (s.genre || s.releaseDate) html += '<div class="meta">' + esc([s.genre, s.releaseDate].filter(Boolean).join(" · ")) + '</div>';
            if (s.plot) html += '<div class="plot">' + esc(s.plot) + '</div>';
            html += '</div></div>';

            for (const season of s.seasons) {
              html += '<div class="season-header">Season ' + season.season + '</div>';
              for (const ep of season.episodes) {
                html += '<div class="list-item episode-row tap' + (ep.isWatched ? ' watched' : '') + '" data-ep="' + esc(ep.sourceEpisodeId) + '">' +
                  '<div class="info"><div class="name">' + esc(ep.title) + '</div></div>' +
                  '<button class="star-btn watched-toggle" data-ep="' + esc(ep.sourceEpisodeId) + '">' + (ep.isWatched ? "✔" : "○") + '</button></div>';
              }
            }

            view.innerHTML = html;
            view.querySelector(".back-btn").onclick = closeSeriesDetail;
            view.querySelectorAll(".episode-row").forEach(row => {
              row.onclick = async (e) => {
                if (e.target.classList.contains("watched-toggle")) return;
                const ok = await apiAction(
                  "/api/series/" + id + "/episodes/" + encodeURIComponent(row.dataset.ep) + "/play",
                  { method: "POST" }, "Couldn't play that episode.");
                if (ok) goToNowPlaying();
              };
            });
            view.querySelectorAll(".watched-toggle").forEach(btn => {
              btn.onclick = async (e) => {
                e.stopPropagation();
                const nowWatched = btn.textContent.trim() !== "✔";
                const ok = await apiAction("/api/series/" + id + "/episodes/" + encodeURIComponent(btn.dataset.ep) + "/watched", {
                  method: "POST", body: JSON.stringify({ watched: nowWatched })
                }, "Couldn't update watched status.");
                if (ok) {
                  btn.textContent = nowWatched ? "✔" : "○";
                  btn.closest(".episode-row").classList.toggle("watched", nowWatched);
                }
              };
            });
          }

          function closeSeriesDetail() {
            document.getElementById("series-detail-view").style.display = "none";
            document.getElementById("series-list-view").style.display = "";
          }

          seriesSearch.addEventListener("input", debounce(loadSeriesList, 300));
          seriesCategory.addEventListener("change", loadSeriesList);

          // ---------- Movies ----------
          let moviesLoaded = false;
          const moviesSearch = document.getElementById("movies-search");
          const moviesCategory = document.getElementById("movies-category");

          async function loadMoviesList() {
            const params = new URLSearchParams({ search: moviesSearch.value, category: moviesCategory.value });
            let data;
            try {
              const res = await api("/api/movies?" + params);
              if (!res.ok) throw new Error("movies " + res.status);
              data = await res.json();
            } catch (e) {
              showToast("Couldn't load movies.");
              return;
            }
            moviesLoaded = true;
            populateCategorySelect(moviesCategory, data.categories);
            const list = document.getElementById("movies-list");
            list.innerHTML = "";
            if (data.truncated) {
              const note = document.createElement("div");
              note.className = "truncated-note";
              note.textContent = "Showing first 200 results - refine your search for more.";
              list.appendChild(note);
            }
            if (data.items.length === 0) {
              list.innerHTML += '<div class="empty-note">No movies found.</div>';
              return;
            }
            for (const item of data.items) {
              const row = document.createElement("div");
              row.className = "list-item tap" + (item.isWatched ? " watched" : "");
              row.innerHTML =
                '<img class="cover" src="' + esc(item.coverUrl || "") + '" onerror="this.style.visibility=\'hidden\'" />' +
                '<div class="info"><div class="name">' + esc(item.name) + '</div></div>';
              row.onclick = () => openMovieDetail(item.id);
              row.appendChild(starButton("movie", item.id, item.isFavorite));
              list.appendChild(row);
            }
          }

          async function openMovieDetail(id) {
            showTab("movies");
            document.getElementById("movies-list-view").style.display = "none";
            const view = document.getElementById("movies-detail-view");
            view.style.display = "";
            view.innerHTML = '<button class="back-btn">&larr; Back to Movies</button><div class="empty-note">Loading...</div>';
            view.querySelector(".back-btn").onclick = closeMovieDetail;

            let m;
            try {
              const res = await api("/api/movies/" + id);
              if (!res.ok) throw new Error("movie detail " + res.status);
              m = await res.json();
            } catch (e) {
              view.innerHTML = '<button class="back-btn">&larr; Back to Movies</button><div class="empty-note">Failed to load.</div>';
              view.querySelector(".back-btn").onclick = closeMovieDetail;
              return;
            }

            let html = '<button class="back-btn">&larr; Back to Movies</button>';
            html += '<div class="detail-header"><img class="cover" src="' + esc(m.coverUrl || "") + '" onerror="this.style.visibility=\'hidden\'" />';
            html += '<div><div class="name">' + esc(m.name) + '</div>';
            const metaParts = [m.genre, m.releaseDate, m.duration].filter(Boolean);
            if (metaParts.length) html += '<div class="meta">' + esc(metaParts.join(" · ")) + '</div>';
            if (m.plot) html += '<div class="plot">' + esc(m.plot) + '</div>';
            html += '</div></div>';
            html += '<button class="play-btn" id="movie-play-btn">▶ Play</button>';

            view.innerHTML = html;
            view.querySelector(".back-btn").onclick = closeMovieDetail;
            document.getElementById("movie-play-btn").onclick = async () => {
              const ok = await apiAction("/api/movies/" + id + "/play", { method: "POST" }, "Couldn't play that movie.");
              if (ok) goToNowPlaying();
            };
          }

          function closeMovieDetail() {
            document.getElementById("movies-detail-view").style.display = "none";
            document.getElementById("movies-list-view").style.display = "";
          }

          moviesSearch.addEventListener("input", debounce(loadMoviesList, 300));
          moviesCategory.addEventListener("change", loadMoviesList);

          // ---------- Favorites ----------
          async function loadFavorites() {
            let data;
            try {
              const res = await api("/api/favorites");
              if (!res.ok) throw new Error("favorites " + res.status);
              data = await res.json();
            } catch (e) {
              showToast("Couldn't load favorites.");
              return;
            }
            const list = document.getElementById("favorites-list");
            list.innerHTML = "";

            if (data.continueWatching.length === 0 && data.channels.length === 0 && data.series.length === 0 && data.movies.length === 0) {
              list.innerHTML = '<div class="empty-note">Nothing favorited yet.</div>';
              return;
            }

            if (data.continueWatching.length > 0) {
              list.insertAdjacentHTML("beforeend", '<div class="section-header">Continue Watching</div>');
              for (const p of data.continueWatching) {
                const row = document.createElement("div");
                row.className = "list-item tap";
                row.innerHTML =
                  '<img class="cover" src="' + esc(p.coverUrl || "") + '" onerror="this.style.visibility=\'hidden\'" />' +
                  '<div class="info"><div class="name">' + esc(p.title) + '</div>' +
                  '<div class="progress-bar"><div class="fill" style="width:' + Math.round(p.progressFraction * 100) + '%"></div></div></div>';
                row.onclick = async () => {
                  const ok = await apiAction("/api/favorites/resume",
                    { method: "POST", body: JSON.stringify({ profileId: p.profileId, contentType: p.contentType, contentKey: p.contentKey }) },
                    "Couldn't resume that.");
                  if (ok) goToNowPlaying();
                };
                list.appendChild(row);
              }
            }

            if (data.channels.length > 0) {
              list.insertAdjacentHTML("beforeend", '<div class="section-header">Channels</div>');
              for (const item of data.channels) {
                const row = document.createElement("div");
                row.className = "list-item tap";
                row.innerHTML =
                  '<img src="' + esc(item.logoUrl || "") + '" onerror="this.style.visibility=\'hidden\'" />' +
                  '<div class="info"><div class="name">' + esc(item.name) + '</div>' +
                  (item.nowTitle ? '<div class="sub">' + esc(item.nowTitle) + '</div>' : '') + '</div>';
                row.onclick = () => playChannel(item.id, "/api/livetv/play");
                row.appendChild(starButton("channel", item.id, true, (fav) => { if (!fav) loadFavorites(); }));
                list.appendChild(row);
              }
            }

            if (data.series.length > 0) {
              list.insertAdjacentHTML("beforeend", '<div class="section-header">Series</div>');
              for (const item of data.series) {
                const row = document.createElement("div");
                row.className = "list-item tap";
                row.innerHTML =
                  '<img class="cover" src="' + esc(item.coverUrl || "") + '" onerror="this.style.visibility=\'hidden\'" />' +
                  '<div class="info"><div class="name">' + esc(item.name) + '</div></div>';
                row.onclick = () => openSeriesDetail(item.id);
                row.appendChild(starButton("series", item.id, true, (fav) => { if (!fav) loadFavorites(); }));
                list.appendChild(row);
              }
            }

            if (data.movies.length > 0) {
              list.insertAdjacentHTML("beforeend", '<div class="section-header">Movies</div>');
              for (const item of data.movies) {
                const row = document.createElement("div");
                row.className = "list-item tap";
                row.innerHTML =
                  '<img class="cover" src="' + esc(item.coverUrl || "") + '" onerror="this.style.visibility=\'hidden\'" />' +
                  '<div class="info"><div class="name">' + esc(item.name) + '</div></div>';
                row.onclick = () => openMovieDetail(item.id);
                row.appendChild(starButton("movie", item.id, true, (fav) => { if (!fav) loadFavorites(); }));
                list.appendChild(row);
              }
            }
          }
        </script>
        </body>
        </html>
        """;
}
