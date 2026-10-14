package main

import (
	"io"
	"log"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"regexp"
	"strconv"
	"strings"
	"time"

	"github.com/alaingilbert/ogame/pkg/ogame"
	"github.com/alaingilbert/ogame/pkg/utils"
	"github.com/alaingilbert/ogame/pkg/wrapper"
	"github.com/labstack/echo/v4"
)

func PostToggleManualModeHandler(c echo.Context) error {
	bot := c.Get("bot").(*wrapper.OGame)
	botMap := c.Get("botMap").(map[string]wrapper.Prioritizable)
	txTimerMap := c.Get("manualModeTimeout").(map[string]*time.Timer)
	txTimer := txTimerMap["timeout"]

	locked, state := bot.GetState()
	if locked && state == "manual mode" {
		txTimer.Stop()
		tx := botMap["tx"]
		tx.Done()
		return c.JSON(http.StatusOK, false)
	} else {
		txTimer = time.NewTimer(30 * time.Second)
		txTimerMap["timeout"] = txTimer
		tx := bot.BeginNamed("manual mode")
		botMap["tx"] = tx
		go ManualTimerTx(botMap, txTimer)
		return c.JSON(http.StatusOK, true)
	}
}

// GetAllianceClassHandler ...
func GetAllianceClassHandler(c echo.Context) error {
	bot := c.Get("bot").(*wrapper.OGame)
	allianceClass, err := bot.GetCachedAllianceClass()
	if err != nil {
		return c.JSON(http.StatusInternalServerError, wrapper.ErrorResp(http.StatusInternalServerError, err.Error()))
	}
	return c.JSON(http.StatusOK, wrapper.SuccessResp(allianceClass))

}

// GetExpeditionMessagesHandler ...
func GetExpeditionMessagesHandler(c echo.Context) error {
	bot := c.Get("bot").(*wrapper.OGame)
	messages, err := bot.GetExpeditionMessages(-1)
	if err != nil {
		return c.JSON(http.StatusInternalServerError, wrapper.ErrorResp(http.StatusInternalServerError, err.Error()))
	}
	return c.JSON(http.StatusOK, wrapper.SuccessResp(messages))

}

// GetFromGameHandler ...
func GetFromGameHandler(c echo.Context) error {
	bot := c.Get("bot").(*wrapper.OGame)
	botMap := c.Get("botMap").(map[string]wrapper.Prioritizable)
	txTimerMap := c.Get("manualModeTimeout").(map[string]*time.Timer)
	txTimer := txTimerMap["timeout"]

	vals := url.Values{"page": {"ingame"}, "component": {"overview"}}
	if len(c.QueryParams()) > 0 {
		vals = c.QueryParams()
	}
	locked, state := bot.GetState()
	var pageHTML []byte
	if locked && state == "manual mode" {
		tx := botMap["tx"]
		txTimer.Reset(30 * time.Second)
		pageHTML, _ = tx.GetPageContent(vals)
	} else {
		pageHTML, _ = bot.GetPageContent(vals)
	}
	//pageHTML = replaceHostname(bot, pageHTML)
	pageHTML = replaceHostnameWithRegxp(bot, pageHTML, c.Request())
	pageHTML = removeCookiesBanner(pageHTML)
	pageHTML = ingameUi(pageHTML, state, locked)
	//pageHTML = injectJS(pageHTML)

	return c.HTMLBlob(http.StatusOK, pageHTML)
}

// PostToGameHandler ...
func PostToGameHandler(c echo.Context) error {
	bot := c.Get("bot").(*wrapper.OGame)
	botMap := c.Get("botMap").(map[string]wrapper.Prioritizable)
	txTimerMap := c.Get("manualModeTimeout").(map[string]*time.Timer)
	txTimer := txTimerMap["timeout"]

	vals := url.Values{"page": {"ingame"}, "component": {"overview"}}
	if len(c.QueryParams()) > 0 {
		vals = c.QueryParams()
	}
	payload, _ := c.FormParams()
	locked, state := bot.GetState()
	var pageHTML []byte
	if state == "manual mode" && locked {
		txTimer.Reset(30 * time.Second)
		tx := botMap["tx"]
		pageHTML, _ = tx.PostPageContent(vals, payload)
	} else {
		pageHTML, _ = bot.PostPageContent(vals, payload)
	}
	//pageHTML = replaceHostname(bot, pageHTML)
	pageHTML = replaceHostnameWithRegxp(bot, pageHTML, c.Request())
	pageHTML = removeCookiesBanner(pageHTML)
	pageHTML = ingameUi(pageHTML, state, locked)
	//pageHTML = injectJS(pageHTML)

	return c.HTMLBlob(http.StatusOK, pageHTML)
}

// GetStaticHEADHandler ...
func GetStaticHEADHandler(c echo.Context) error {
	bot := c.Get("bot").(*wrapper.OGame)
	newURL := "/api/" + strings.Join(c.ParamValues(), "") // + "?" + c.QueryString()
	if len(c.QueryString()) > 0 {
		newURL = newURL + "?" + c.QueryString()
	}
	headers, err := bot.HeadersForPage(newURL)
	if err != nil {
		return c.JSON(http.StatusBadRequest, wrapper.ErrorResp(400, err.Error()))
	}
	if len(headers) < 1 {
		return c.NoContent(http.StatusFailedDependency)
	}
	// Copy the original HTTP HEAD headers to our client
	for k, vv := range headers { // duplicate headers are acceptable in HTTP spec, so add all of them individually: https://stackoverflow.com/questions/4371328/are-duplicate-http-response-headers-acceptable
		k = http.CanonicalHeaderKey(k)
		for _, v := range vv {
			c.Response().Header().Add(k, v)
		}
	}
	return c.NoContent(http.StatusOK)
}

// GetStaticHandler ...
func GetStaticHandler(c echo.Context) error {
	bot := c.Get("bot").(*wrapper.OGame)

	newURL := bot.ServerURL() + c.Request().URL.String()
	req, err := http.NewRequest(http.MethodGet, newURL, nil)
	if err != nil {
		return c.JSON(http.StatusInternalServerError, wrapper.ErrorResp(500, err.Error()))
	}
	req.Header.Add("Accept-Encoding", "gzip, deflate, br")
	resp, err := bot.GetDevice().GetClient().Do(req)
	if err != nil {
		return c.JSON(http.StatusInternalServerError, wrapper.ErrorResp(500, err.Error()))
	}
	defer resp.Body.Close()
	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return c.JSON(http.StatusInternalServerError, wrapper.ErrorResp(500, err.Error()))
	}

	// Copy the original HTTP headers to our client
	for k, vv := range resp.Header { // duplicate headers are acceptable in HTTP spec, so add all of them individually: https://stackoverflow.com/questions/4371328/are-duplicate-http-response-headers-acceptable
		k = http.CanonicalHeaderKey(k)
		if k != "Content-Length" && k != "Content-Encoding" { // https://github.com/alaingilbert/ogame/pull/80#issuecomment-674559853
			for _, v := range vv {
				c.Response().Header().Add(k, v)
			}
		}
	}

	if strings.Contains(c.Request().URL.String(), ".xml") {
		//body = replaceHostname(bot, body)
		body = replaceHostnameWithRegxp(bot, body, c.Request())
		return c.Blob(http.StatusOK, "application/xml", body)
	}

	contentType := http.DetectContentType(body)
	if strings.Contains(newURL, ".css") {
		contentType = "text/css"
	} else if strings.Contains(newURL, ".js") {
		contentType = "application/javascript"
	} else if strings.Contains(newURL, ".gif") {
		contentType = "image/gif"
	}

	return c.Blob(http.StatusOK, contentType, body)
}

func replaceHostnameWithRegxp(bot *wrapper.OGame, pageHTML []byte, r *http.Request) []byte {
	requestHostname := "http://" + r.Host
	if r.TLS != nil {
		requestHostname = "https://" + r.Host
	}
	regexes := []string{
		`https://s\d+-[a-z]{2}\.ogame\.gameforge\.com`,
		`https:\\/\\/s\d+-[a-z]{2}\.ogame\.gameforge\.com`,
		`https:\\\\\\\/\\\\\\\/s\d+-[a-z]{2}\.ogame\.gameforge\.com`,
	}
	for idx, regex := range regexes {
		replaceStr := requestHostname
		// if bot.apiNewHostname != "" {
		// 	replaceStr = bot.apiNewHostname
		// }

		if idx > 0 {
			replaceStr = strings.ReplaceAll(replaceStr, `/`, `\/`)
			if idx > 1 {
				replaceStr = strings.ReplaceAll(replaceStr, `/`, `\\\/`)
			}
		}

		pageHTML = regexp.MustCompile(regex).ReplaceAll(pageHTML, []byte(replaceStr))
	}
	return pageHTML
}

func removeCookiesBanner(pageHTML []byte) []byte {
	pageHTMLstr := string(pageHTML)
	pageHTMLstr = strings.ReplaceAll(pageHTMLstr, "https://consent.gameforge.com/cookiebanner.js", "")
	pageHTMLstr = strings.ReplaceAll(pageHTMLstr, "https://consent-sandbox.gameforge.com/cookiebanner.js", "")
	// regex := `<script[^>]*id="cookiebanner"[^>]*>[\s\S]*?</script>`
	// re := regexp.MustCompile(regex)
	// return re.ReplaceAll(pageHTML, []byte(""))
	return []byte(pageHTMLstr)
}

func ingameUi(pageHTML []byte, state string, locked bool) []byte {
	tmpHTML := string(pageHTML)

	toReplace := `<script>
	var btn1 = document.createElement("BUTTON");`
	if state == "manual mode" && locked {
		toReplace += `
        var t = document.createTextNode("Manual mode (on)");
		`
	} else {
		toReplace += `
        var t = document.createTextNode("Manual mode (off)");
		`
	}
	toReplace += `
        btn1.style.height = '20px';
        btn1.style.width = '130px';
        btn1.style.display = 'block';
        btn1.style.marginBottom = '3px';
        btn1.appendChild(t);
        btn1.onclick = function() {
            var formData = new FormData();
            $.ajax({
                url: "/bot/toggle-manual-mode", data: formData, type: 'POST', processData: false, contentType: false,
                success: function(res) {
                    $(btn1).text("Manual mode (" + (res ? "on" : "off") + ")");
                },
                error: function(req) { console.log(req.responseText); },
            });
        };

		var div = document.createElement("div");
        div.style.position = 'fixed';
        div.style.right = '10px';
        div.style.top = '10px';
        div.style.zIndex = '3001';
        div.appendChild(btn1);

        // Kacheln-Schalter: blendet Sleep-/Alarm- und Einstellungskachel aus bzw. ein (Zustand bleibt im Browser gespeichert)
        var kOff = false;
        try { kOff = (localStorage.getItem('tbotKachelnAus') === '1'); } catch (e) { kOff = false; }
        var btnK = document.createElement("BUTTON");
        btnK.style.cssText = 'height:20px;width:130px;display:block;margin-bottom:3px;';
        function kApply() {
            btnK.textContent = 'Tiles (' + (kOff ? 'off' : 'on') + ')';
            divL.style.display = kOff ? 'none' : 'block';
            if (kOff) { tile.style.display = 'none'; sTile.style.display = 'none'; }
            else { tRender(); sRender(); }
        }
        btnK.onclick = function() {
            kOff = !kOff;
            try { localStorage.setItem('tbotKachelnAus', kOff ? '1' : '0'); } catch (e) { }
            kApply();
        };
        div.appendChild(btnK);

        // --- Sleep-/Recall-Kachel (liest /bot/sleepstatus, von TBot alle 30 s geschrieben) ---
        var tile = document.createElement("div");
        tile.style.cssText = 'display:none;width:150px;margin-top:4px;padding:6px 8px;background:#5b3a1e;border:1px solid #8a5a2b;border-radius:4px;color:#f3e3c7;font:11px/1.35 Arial,sans-serif;box-shadow:0 1px 4px rgba(0,0,0,.5);';
        div.appendChild(tile);
        var st = null;
        function tFmt(sec) {
            if (sec < 0) { sec = 0; }
            var h = Math.floor(sec / 3600), m = Math.floor((sec % 3600) / 60);
            return h + ':' + (m < 10 ? '0' : '') + m;
        }
        function tEsc(x) { return String(x).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;'); }
        function tCoord(c) {
            c = String(c).replace(/^\[|\]$/g, '').replace(/^Moon:/, 'M:').replace(/^Planet:/, 'P:');
            return '[' + tEsc(c) + ']';
        }
        function tCd(sec) {
            if (sec < 0) { sec = 0; }
            sec = Math.floor(sec);
            var h = Math.floor(sec / 3600), m = Math.floor((sec % 3600) / 60), x = sec % 60;
            var mm = (h > 0 && m < 10 ? '0' : '') + m;
            return (h > 0 ? h + ':' : '') + mm + ':' + (x < 10 ? '0' : '') + x;
        }
        function tMis(m) {
            if (m === 'GroupedAttack') { return 'ACS'; }
            if (m === 'MissileAttack') { return 'Missile'; }
            if (m === 'Destroy') { return 'Destroy'; }
            return 'Attack';
        }
        function tRender() {
            if (kOff) { tile.style.display = 'none'; return; }
            if (!st) { tile.style.display = 'none'; return; }
            var el = (Date.now() - st.updatedMs) / 1000;
            if (el > 180) { tile.style.display = 'none'; return; } // Datei veraltet -> TBot laeuft nicht
            var rec = st.recalls || [];
            var att = st.attacks || [];
            if (!st.sleeping && rec.length === 0 && att.length === 0) { tile.style.display = 'none'; return; }
            var h = '';
            if (att.length) {
                h += '<div style="color:#ffb3b3"><b>INCOMING ATTACK</b></div>';
                for (var k = 0; k < att.length; k++) {
                    h += '<div>' + tMis(att[k].mission) + ' ' + tCoord(att[k].dest) + '</div>';
                    h += '<div style="color:#ffd2d2">&nbsp;&nbsp;' + tEsc(att[k].at) + ' (in ' + tCd(att[k].inSec - el) + ')</div>';
                }
                if (st.attacksMore > 0) { h += '<div>and ' + st.attacksMore + ' more</div>'; }
            }
            if (st.sleeping) { h += '<div' + (att.length ? ' style="margin-top:3px"' : '') + '><b>' + (st.mode === 'GhostSleep' ? 'Ghost sleep on' : 'Sleep mode on') + '</b></div>'; }
            if (rec.length) {
                h += '<div style="margin-top:3px"><u>Recalls due</u></div>';
                for (var i = 0; i < rec.length; i++) { h += '<div>' + tCoord(rec[i].coord) + ' ' + tEsc(rec[i].at) + '</div>'; }
                if (st.more > 0) { h += '<div>and ' + st.more + ' more</div>'; }
            }
            if (st.sleeping && st.wakeInSec != null) {
                h += '<div style="margin-top:3px">Wake up in ' + tFmt(st.wakeInSec - el) + '</div>';
            } else if (!st.sleeping && st.sleepInSec != null) {
                h += '<div style="margin-top:3px">Back to sleep in ' + tFmt(st.sleepInSec - el) + '</div>';
            }
            tile.style.background = att.length ? '#6b1414' : '#5b3a1e';
            tile.style.borderColor = att.length ? '#d04040' : '#8a5a2b';
            tile.innerHTML = h;
            tile.style.display = 'block';
        }
        function tLoad() {
            $.ajax({ url: "/bot/sleepstatus", type: 'GET', cache: false, dataType: 'json',
                success: function(r) { st = r; tRender(); }, error: function() {} });
        }
        setInterval(tRender, 1000);
        setInterval(tLoad, 10000);
        tLoad();
        // --- Ende Kachel ---

        // --- Slot-Kachel: belegte/freie Slots + Limits live aendern (POST /bot/slot-limit, TBot setzt es ohne Neustart) ---
        var sTile = document.createElement("div");
        sTile.style.cssText = 'display:none;width:190px;margin-top:4px;padding:6px 8px;background:#1e3a5b;border:1px solid #2b5a8a;border-radius:4px;color:#e3eef7;font:11px/1.35 Arial,sans-serif;box-shadow:0 1px 4px rgba(0,0,0,.5);';
        // linke Spalte: Einstellungskachel liegt links ausserhalb des OGame-Fensters (Position siehe tPlace)
        var divL = document.createElement("div");
        divL.style.position = 'fixed';
        divL.style.left = '8px';
        divL.style.top = '10px';
        divL.style.zIndex = '3001';
        divL.appendChild(sTile);
        var sHead = document.createElement("div");
        sHead.style.cssText = 'font-weight:bold;margin-bottom:3px;';
        sTile.appendChild(sHead);
        var sRowsDef = [
            { key: 'autofarm',  label: 'AutoFarm' },
            { key: 'brain',     label: 'Brain' },
            { key: 'harvest',   label: 'Harvest' },
            { key: 'discovery', label: 'Discovery' },
            { key: 'exp',       label: 'Exp' },
            { key: 'leaveFree', label: 'Keep free' }
        ];
        var sRows = {};
        function sSend(key, inp, info) {
            var v = parseInt(inp.value, 10);
            if (isNaN(v) || v < 0) { return; }
            info.textContent = '...';
            $.ajax({ url: "/bot/slot-limit", type: 'POST', data: { key: key, value: v }, dataType: 'json',
                success: function() { inp.dataset.dirty = ''; info.textContent = 'ok'; setTimeout(tLoad, 4000); setTimeout(function() { info.textContent = ''; }, 3000); },
                error: function() { info.textContent = 'Error'; } });
        }
        sRowsDef.forEach(function(def) {
            var row = document.createElement("div");
            row.style.cssText = 'display:flex;align-items:center;gap:3px;margin-top:2px;';
            var lab = document.createElement("span");
            lab.style.cssText = 'flex:1 1 auto;white-space:nowrap;';
            lab.textContent = def.label;
            var use = document.createElement("span");
            use.style.cssText = 'min-width:12px;text-align:right;color:#9fd0ff;';
            var inp = document.createElement("input");
            inp.type = 'number'; inp.min = '0'; inp.max = '40';
            inp.style.cssText = 'width:34px;font:11px Arial,sans-serif;padding:0 2px;background:#0f2238;color:#e3eef7;border:1px solid #2b5a8a;border-radius:2px;';
            var btn = document.createElement("button");
            btn.textContent = 'OK';
            btn.style.cssText = 'font:10px Arial,sans-serif;padding:0 3px;cursor:pointer;';
            var info = document.createElement("span");
            info.style.cssText = 'font-size:9px;color:#9fd0ff;';
            inp.addEventListener('input', function() { inp.dataset.dirty = '1'; });
            inp.addEventListener('keydown', function(ev) { if (ev.key === 'Enter') { sSend(def.key, inp, info); } });
            btn.addEventListener('click', function() { sSend(def.key, inp, info); });
            row.appendChild(lab);
            if (def.key !== 'leaveFree') { row.appendChild(use); }
            row.appendChild(inp);
            row.appendChild(btn);
            sTile.appendChild(row);
            sTile.appendChild(info);
            sRows[def.key] = { use: use, inp: inp };
        });
        // Worker-Schalter (an/aus): setzt das Active-Flag des Workers live
        var wDef = [
            { key: 'w_autofarm',    id: 'autofarm',    label: 'AutoFarm',    pkey: 'p_autofarm' },
            { key: 'w_expeditions', id: 'expeditions', label: 'Expeditions', pkey: 'p_expeditions' },
            { key: 'w_harvest',     id: 'harvest',     label: 'Harvest',     pkey: 'p_harvest' },
            { key: 'w_discovery',   id: 'discovery',   label: 'Discovery',   pkey: 'p_discovery' },
            { key: 'w_brain',       id: 'brain',       label: 'Brain',       pkey: 'p_brain' },
            { key: 'w_auction',     id: 'auction',     label: 'Auction' }
        ];
        var wBtns = {};
        var wTitle = document.createElement("div");
        wTitle.style.cssText = 'margin-top:5px;border-top:1px solid #2b5a8a;padding-top:3px;font-weight:bold;';
        wTitle.textContent = 'Workers (priority: lower = more important)';
        sTile.appendChild(wTitle);
        // pro Worker eine Zeile wie bei den Slots: Name | On | Off (aktiver Zustand hervorgehoben)
        function wSend(d, on, row) {
            wBtns[d.id].on.disabled = true; wBtns[d.id].off.disabled = true;
            $.ajax({ url: "/bot/slot-limit", type: 'POST', data: { key: d.key, value: on ? 1 : 0 }, dataType: 'json',
                success: function() { setTimeout(function() { wBtns[d.id].on.disabled = false; wBtns[d.id].off.disabled = false; tLoad(); }, 4000); },
                error: function() { wBtns[d.id].on.disabled = false; wBtns[d.id].off.disabled = false; } });
        }
        wDef.forEach(function(d) {
            var row = document.createElement("div");
            row.style.cssText = 'display:flex;align-items:center;gap:3px;margin-top:2px;';
            var lab = document.createElement("span");
            lab.style.cssText = 'flex:1 1 auto;white-space:nowrap;';
            lab.textContent = d.label;
            var bOn = document.createElement("button");
            bOn.textContent = 'On';
            var bOff = document.createElement("button");
            bOff.textContent = 'Off';
            [bOn, bOff].forEach(function(x) { x.style.cssText = 'font:10px Arial,sans-serif;padding:0 5px;cursor:pointer;border:1px solid #2b5a8a;border-radius:2px;background:#0f2238;color:#9fb8cf;'; });
            bOn.addEventListener('click', function() { if (bOn.dataset.active !== '1') { wSend(d, true); } });
            bOff.addEventListener('click', function() { if (bOff.dataset.active !== '1') { wSend(d, false); } });
            row.appendChild(lab);
            var pInp = null;
            if (d.pkey) {
                // Slot-Prioritaet (General.SlotPriorityLevel): Eingabe, Enter oder Verlassen des Feldes sendet
                pInp = document.createElement("input");
                pInp.type = 'number'; pInp.min = '0'; pInp.max = '40';
                pInp.title = 'Slot priority (lower = more important)';
                pInp.style.cssText = 'width:34px;font:11px Arial,sans-serif;padding:0 2px;background:#0f2238;color:#ffd86b;border:1px solid #2b5a8a;border-radius:2px;';
                var pInfo = document.createElement("span");
                pInfo.style.cssText = 'font-size:9px;color:#9fd0ff;';
                pInp.addEventListener('input', function() { pInp.dataset.dirty = '1'; });
                pInp.addEventListener('keydown', function(ev) { if (ev.key === 'Enter') { sSend(d.pkey, pInp, pInfo); pInp.blur(); } });
                pInp.addEventListener('change', function() { sSend(d.pkey, pInp, pInfo); });
                row.appendChild(pInp);
            }
            row.appendChild(bOn);
            row.appendChild(bOff);
            sTile.appendChild(row);
            wBtns[d.id] = { row: row, on: bOn, off: bOff, prio: pInp };
        });
        function sRender() {
            if (kOff) { sTile.style.display = 'none'; return; }
            if (!st || !st.slots || (Date.now() - st.updatedMs) / 1000 > 180) { sTile.style.display = 'none'; return; }
            var s = st.slots;
            sHead.textContent = 'Slots ' + s.inUse + '/' + s.total + ' (free ' + s.free + ')';
            var use = st.slotUse || {}, lim = st.slotLimits || {};
            use.exp = s.expInUse;
            sRowsDef.forEach(function(def) {
                var r = sRows[def.key];
                if (use[def.key] != null) { r.use.textContent = use[def.key] + '/'; }
                if (lim[def.key] != null && document.activeElement !== r.inp && r.inp.dataset.dirty !== '1') { r.inp.value = lim[def.key]; }
            });
            var wk = st.workers || {};
            wDef.forEach(function(d) {
                var w = wBtns[d.id], v = wk[d.id];
                if (v === null || v === undefined) { w.row.style.display = 'none'; return; }
                w.row.style.display = 'flex';
                var pr = st.priorities || {};
                if (w.prio && pr[d.id] != null && document.activeElement !== w.prio && w.prio.dataset.dirty !== '1') { w.prio.value = pr[d.id]; }
                w.on.dataset.active = v ? '1' : '0';
                w.off.dataset.active = v ? '0' : '1';
                w.on.style.background = v ? '#1f6b2f' : '#0f2238';
                w.on.style.color = v ? '#fff' : '#9fb8cf';
                w.off.style.background = v ? '#0f2238' : '#6b1f1f';
                w.off.style.color = v ? '#9fb8cf' : '#fff';
            });
            sTile.style.display = 'block';
        }
        setInterval(sRender, 2000);
        // --- Ende Slot-Kachel ---

		document.body.prepend(divL);
		document.body.prepend(div);
        kApply();

        // --- Kacheln mit kleinem Abstand ans OGame-Fenster legen (statt an den Browserrand) ---
        // Sucht den Spielbereich (Container) und setzt die rechte Spalte (Manual mode + Sleep/Alarm) mit 8 px Abstand
        // rechts daneben, die Einstellungskachel mit 8 px Abstand links daneben. Passt nichts daneben (schmales
        // Fenster) oder wird kein Container gefunden, bleibt es beim Browserrand mit 10 px.
        var T_GAP = 8;
        var tLogged = false;
        function tGameRect() {
            var sels = ['#wrapper', '#ingamepage', '#contentWrapper', '#pageContent', '#pagecontent', '#mainContent', '#middle', '#bar'];
            for (var i = 0; i < sels.length; i++) {
                var el = document.querySelector(sels[i]);
                if (!el) { continue; }
                var r = el.getBoundingClientRect();
                if (r.width >= 700 && r.width <= window.innerWidth - 40) { return { r: r, sel: sels[i] }; }
            }
            var a = document.querySelector('#links, #menuTable'), b = document.querySelector('#rechts');
            if (a && b) {
                var ra = a.getBoundingClientRect(), rb = b.getBoundingClientRect();
                if (rb.right - ra.left >= 700 && rb.right - ra.left <= window.innerWidth - 40) {
                    return { r: { left: ra.left, right: rb.right, top: Math.min(ra.top, rb.top) }, sel: '#links+#rechts' };
                }
            }
            return null;
        }
        function tPlace() {
            var g = tGameRect();
            if (!g) {
                div.style.left = 'auto'; div.style.right = '10px';
                divL.style.left = '8px';
                return;
            }
            if (!tLogged) { console.log('[TBot-tiles] game area:', g.sel, g.r.left, g.r.right); tLogged = true; }
            var wR = div.offsetWidth || 160, wL = divL.offsetWidth || 170;
            var rightLeft = g.r.right + T_GAP;
            if (rightLeft + wR > window.innerWidth - 4) {
                div.style.left = 'auto'; div.style.right = '10px';
            } else {
                div.style.right = 'auto'; div.style.left = rightLeft + 'px';
            }
            var leftLeft = g.r.left - T_GAP - wL;
            divL.style.left = Math.max(4, leftLeft) + 'px';
        }
        tPlace();
        setInterval(tPlace, 2000);
        window.addEventListener('resize', tPlace);

		</script>`

	toReplace += `</body>`

	tmpHTML = strings.ReplaceAll(tmpHTML, `</body>`, toReplace)
	return []byte(tmpHTML)
}

// GetLfBonusesHandler ...
func GetLfBonusesTbotHandler(c echo.Context) error {
	bot := c.Get("bot").(*wrapper.OGame)
	planetID, err := utils.ParseI64(c.Param("planetID"))
	if err != nil {
		return c.JSON(http.StatusBadRequest, wrapper.ErrorResp(400, "invalid planet id"))
	}
	opts := getOptions()
	opts.ChangePlanet = ogame.CelestialID(planetID)
	res, err := getLfBonuses(bot, opts)
	if err != nil {
		return c.JSON(http.StatusInternalServerError, wrapper.ErrorResp(500, err.Error()))
	}
	return c.JSON(http.StatusOK, wrapper.SuccessResp(res))
}

func getLfBonuses(b *wrapper.OGame, opts wrapper.Options) (out ogame.LfBonuses, err error) {
	page := wrapper.LfBonusesPageName
	vals := url.Values{"page": {"ingame"}, "component": {page}, "cp": {utils.DoCastStr(opts.ChangePlanet)}}
	if page == wrapper.FetchResourcesPageName || page == wrapper.FetchTechsName || page == wrapper.LogoutPageName {
		vals = url.Values{"page": {page}}
	}
	pageHtml, err := b.GetPageContent(vals)
	if err != nil {
		return
	}
	bonuses, err := ExtractLfBonuses(pageHtml)
	if err != nil {
		return
	}
	return bonuses, nil
}

func getOptions(opts ...wrapper.Option) (out wrapper.Options) {
	for _, opt := range opts {
		if opt != nil {
			opt(&out)
		}
	}
	return
}

func ManualTimerTx(botMap map[string]wrapper.Prioritizable, txTimer *time.Timer) {
	tx := botMap["tx"]
	select {
	case <-txTimer.C:
		tx.Done()
		log.Println("Manual Mode Expired!")
		// do something for timeout, like change state
	}
}

// GetLfBonusesHandler ...
func GetLfBonusesHandler(c echo.Context) error {
	bot := c.Get("bot").(*wrapper.OGame)
	lfBonuses, err := bot.GetLfBonuses()
	if err != nil {
		return c.JSON(http.StatusInternalServerError, wrapper.ErrorResp(http.StatusInternalServerError, err.Error()))
	}
	return c.JSON(http.StatusOK, wrapper.SuccessResp(lfBonuses))
}

// GetPositionsAvailableForDiscoveryFleet...
// curl 127.0.0.1:1234/bot/planets/:planetID/get-system-available-discovery -d 'galaxy=6&system=473'
func GetPositionsAvailableForDiscoveryFleet(c echo.Context) error {
	bot := c.Get("bot").(*wrapper.OGame)
	planetID, err := utils.ParseI64(c.Param("planetID"))
	if err != nil {
		return c.JSON(http.StatusBadRequest, wrapper.ErrorResp(400, "invalid planet id"))
	}
	var opts wrapper.Option
	if planetID != 0 {
		opts = wrapper.ChangePlanet(ogame.CelestialID(planetID))
	}

	if err := c.Request().ParseForm(); err != nil {
		return c.JSON(http.StatusBadRequest, wrapper.ErrorResp(400, "invalid form"))
	}

	where := ogame.Coordinate{Type: ogame.PlanetType}
	for key, values := range c.Request().PostForm {
		switch key {
		case "galaxy":
			galaxy, err := utils.ParseI64(values[0])
			if err != nil {
				return c.JSON(http.StatusBadRequest, wrapper.ErrorResp(400, "invalid galaxy"))
			}
			where.Galaxy = galaxy
		case "system":
			system, err := utils.ParseI64(values[0])
			if err != nil {
				return c.JSON(http.StatusBadRequest, wrapper.ErrorResp(400, "invalid system"))
			}
			where.System = system
		case "position":
			position, err := utils.ParseI64(values[0])
			if err != nil {
				return c.JSON(http.StatusBadRequest, wrapper.ErrorResp(400, "invalid position"))
			}
			where.Position = position
		}
	}
	var coords []ogame.Coordinate
	if coords, err = bot.GetPositionsAvailableForDiscoveryFleet(where.Galaxy, where.System, opts); err != nil {
		return c.JSON(http.StatusBadRequest, wrapper.ErrorResp(400, err.Error()))
	}
	return c.JSON(http.StatusOK, wrapper.SuccessResp(coords))
}

// GetAvailableDiscoveries...
// curl 127.0.0.1:1234/bot/planets/:planetID/get-available-discoveries
func GetAvailableDiscoveries(c echo.Context) error {
	bot := c.Get("bot").(*wrapper.OGame)
	bot.GetAvailableDiscoveries()

	planetID, err := utils.ParseI64(c.Param("planetID"))
	if err != nil {
		return c.JSON(http.StatusBadRequest, wrapper.ErrorResp(400, "invalid planet id"))
	}
	var opts wrapper.Option
	if planetID != 0 {
		opts = wrapper.ChangePlanet(ogame.CelestialID(planetID))
	}

	if err := c.Request().ParseForm(); err != nil {
		return c.JSON(http.StatusBadRequest, wrapper.ErrorResp(400, "invalid form"))
	}

	where := ogame.Coordinate{Type: ogame.PlanetType}
	for key, values := range c.Request().PostForm {
		switch key {
		case "galaxy":
			galaxy, err := utils.ParseI64(values[0])
			if err != nil {
				return c.JSON(http.StatusBadRequest, wrapper.ErrorResp(400, "invalid galaxy"))
			}
			where.Galaxy = galaxy
		case "system":
			system, err := utils.ParseI64(values[0])
			if err != nil {
				return c.JSON(http.StatusBadRequest, wrapper.ErrorResp(400, "invalid system"))
			}
			where.System = system
		case "position":
			position, err := utils.ParseI64(values[0])
			if err != nil {
				return c.JSON(http.StatusBadRequest, wrapper.ErrorResp(400, "invalid position"))
			}
			where.Position = position
		}
	}
	var count int64
	if count, err = bot.GetAvailableDiscoveries(opts); err != nil {
		return c.JSON(http.StatusBadRequest, wrapper.ErrorResp(400, err.Error()))
	}
	return c.JSON(http.StatusOK, wrapper.SuccessResp(count))
}

// SleepStatusHandler liefert die von TBot geschriebene sleepstatus.json (Kachel unter dem Manual-mode-Button).
// Datei liegt neben der ogamed-Binary (/home/tbot/tbot/sleepstatus.json). Fehlt sie, kommt "kein Sleep" zurueck.
func SleepStatusHandler(c echo.Context) error {
	path := "/home/tbot/tbot/sleepstatus.json"
	if exe, err := os.Executable(); err == nil {
		path = filepath.Join(filepath.Dir(exe), "sleepstatus.json")
	}
	data, err := os.ReadFile(path)
	c.Response().Header().Set("Cache-Control", "no-store")
	if err != nil {
		return c.JSONBlob(http.StatusOK, []byte(`{"sleeping":false,"recalls":[],"more":0,"updatedMs":0}`))
	}
	return c.JSONBlob(http.StatusOK, data)
}

// PostSlotLimitHandler nimmt ein Slot-Limit von der Slot-Kachel entgegen und legt es als Befehlsdatei
// slotcmd_<zeit>.json neben die ogamed-Binary (/home/tbot/tbot). TBot liest sie alle 3 s und setzt den Wert live.
// Nur Schluessel aus der Whitelist, Wert 0..40 (w_* = Worker-Schalter, 0=aus, 1=an).
func PostSlotLimitHandler(c echo.Context) error {
	key := c.FormValue("key")
	switch key {
	case "leaveFree", "autofarm", "brain", "harvest", "discovery", "exp",
		"p_autofarm", "p_expeditions", "p_harvest", "p_discovery", "p_brain",
		"w_autofarm", "w_expeditions", "w_harvest", "w_discovery", "w_brain", "w_auction":
	default:
		return c.JSON(http.StatusBadRequest, map[string]string{"error": "unbekannter Schluessel"})
	}
	val, err := strconv.Atoi(c.FormValue("value"))
	if err != nil || val < 0 || val > 40 {
		return c.JSON(http.StatusBadRequest, map[string]string{"error": "Wert muss 0..40 sein"})
	}
	dir := "/home/tbot/tbot"
	if exe, err := os.Executable(); err == nil {
		dir = filepath.Dir(exe)
	}
	name := filepath.Join(dir, "slotcmd_"+strconv.FormatInt(time.Now().UnixNano(), 10)+".json")
	body := []byte(`{"key":"` + key + `","value":` + strconv.Itoa(val) + `}`)
	if err := os.WriteFile(name+".tmp", body, 0644); err != nil {
		return c.JSON(http.StatusInternalServerError, map[string]string{"error": err.Error()})
	}
	if err := os.Rename(name+".tmp", name); err != nil {
		return c.JSON(http.StatusInternalServerError, map[string]string{"error": err.Error()})
	}
	return c.JSON(http.StatusOK, map[string]bool{"ok": true})
}