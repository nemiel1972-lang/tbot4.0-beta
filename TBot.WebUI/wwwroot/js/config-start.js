(function () {
    'use strict';
    var box = document.getElementById('cfg-start');
    var main = document.getElementById('cfg-main');
    if (!box || !main) return;
    var url = box.dataset.url, base = box.dataset.lf, instance = main.dataset.instance;
    var last = null, upd = null, updBusy = false, lfSnap = null, lfAt = 0;

    function el(tag, cls, text) { var e = document.createElement(tag); if (cls) e.className = cls; if (text !== undefined && text !== null) e.textContent = text; return e; }
    function masked() { return window.tbotHideData ? window.tbotHideData.get() : false; }
    function hide(v) { return masked() ? '*****' : v; }
    function num(n) { return (n === null || n === undefined || isNaN(n)) ? '-' : Number(n).toLocaleString('en-US'); }
    function compact(n) {
        if (n === null || n === undefined || isNaN(n)) return '-';
        var a = Math.abs(n), u = [[1e12, 'T'], [1e9, 'B'], [1e6, 'M'], [1e3, 'K']];
        for (var i = 0; i < u.length; i++) if (a >= u[i][0]) return (n / u[i][0]).toFixed(a >= u[i][0] * 100 ? 0 : 1) + u[i][1];
        return String(Math.round(n));
    }
    function dur(sec) {
        sec = Math.max(0, Math.round(sec));
        var h = Math.floor(sec / 3600), m = Math.floor((sec % 3600) / 60), s = sec % 60;
        if (h >= 24) return Math.floor(h / 24) + 'd ' + (h % 24) + 'h';
        return h > 0 ? h + 'h ' + String(m).padStart(2, '0') + 'm' : (m > 0 ? m + 'm ' + String(s).padStart(2, '0') + 's' : s + 's');
    }
    function camelWords(t) { return String(t || '').replace(/([a-z])([A-Z])/g, '$1 $2'); }
    function row(label, value, cls) {
        var r = el('div', 'st-row');
        r.appendChild(el('span', 'st-lbl', label));
        var v = el('b', cls || '', value); r.appendChild(v);
        return r;
    }
    function ageText(r) {
        if (!r) return 'no data';
        return r.newest === r.oldest ? dur(r.newest) + ' old' : dur(r.newest) + ' to ' + dur(r.oldest) + ' old';
    }
    function refreshBar(a) {
        var w = el('div', 'st-mut');
        w.style.marginTop = '6px';
        if (a) {
            w.appendChild(el('div', null, 'Data age: resources ' + ageText(a.res) + ', ships ' + ageText(a.ships) + ', production ' + ageText(a.prod)));
            if (a.note) w.appendChild(el('div', null, a.note));
        }
        var running = !!(a && a.running);
        var b = el('button', 'cfg-btn sm', running ? 'Refreshing…' : 'Refresh now'); b.type = 'button'; b.disabled = running;
        b.title = 'Read resources, ships and production of all planets and moons once. Only press it while you are not playing in the browser.';
        b.addEventListener('click', function () {
            b.disabled = true; b.textContent = 'Refreshing…';
            fetch(box.dataset.refresh + '?instance=' + encodeURIComponent(instance), { method: 'POST' }).then(function () { setTimeout(function () { if (typeof load === 'function') load(); }, 4000); });
        });
        w.appendChild(b);
        return w;
    }
    function symbolFor(name) {
        var n = String(name || '').toLowerCase();
        if (/expedition.*slot|slot.*expedition/.test(n)) return '\uD83E\uDDED';
        if (/slot/.test(n)) return '\uD83D\uDE80';
        if (/turbo|speed|tempo/.test(n)) return '\u26A1';
        if (/computer/.test(n)) return '\uD83D\uDDA5';
        if (/booster|bonus|mine|production/.test(n)) return '\u26CF';
        if (/event/.test(n)) return '\uD83C\uDF89';
        return '\u25C6';
    }
    function itemTile(key, img, name, value, bad, title, ghost) {
        var t = el('div', 'st-itemrow' + (bad ? ' bad' : '') + (ghost ? ' st-ghost' : ''));
        t.dataset.key = key; t.title = title || name;
        var ic = el('div', 'st-item-ic');
        if (img && /^https?:\/\//.test(img)) { var im = el('img'); im.src = img; im.alt = ''; im.addEventListener('error', function () { this.remove(); ic.textContent = symbolFor(name); }); ic.appendChild(im); }
        else ic.textContent = symbolFor(name);
        t.appendChild(ic);
        var tx = el('div', 'st-item-tx'); tx.appendChild(el('span', 'st-item-name', name)); tx.appendChild(el('b', null, ghost ? '\u2014' : value)); t.appendChild(tx);
        return t;
    }
    function knownKey() { return 'tbot.start.items.' + instance; }
    function readKnownItems() { try { var v = JSON.parse(localStorage.getItem(knownKey()) || '{}'); return v && typeof v === 'object' && !Array.isArray(v) ? v : {}; } catch (e) { return {}; } }
    function saveKnownItems(v) { try { localStorage.setItem(knownKey(), JSON.stringify(v)); } catch (e) {  } }
    function getPref(k, dflt) { try { var v = localStorage.getItem('tbot.start.' + k + '.' + instance); return v === null ? dflt : v; } catch (e) { return dflt; } }
    function setPref(k, v) { try { localStorage.setItem('tbot.start.' + k + '.' + instance, v); } catch (e) {  } }
    function isLocked() { return getPref('lock', '0') === '1'; }
    function readAlpha() { var a = Number(getPref('alpha', '100')); return a >= 15 && a <= 100 ? a : 100; }
    function applyAlpha() {
        var a = readAlpha(), r = document.documentElement;
        [box, r].forEach(function (t) { t.style.setProperty('--st-alpha', String(a)); t.style.setProperty('--st-blur', a < 100 ? '4px' : '0px'); });
    }
    function card(title) {
        var c = el('section', 'st-card');
        c.dataset.key = title;
        var h = el('h2'); h.appendChild(el('span', 'st-title', title)); c.appendChild(h);
        var b = el('div', 'st-body'); c.appendChild(b);
        return { root: c, body: b };
    }
    function blockLink(name) {
        return base + '?instance=' + encodeURIComponent(instance) + '&block=' + encodeURIComponent(name);
    }

    function render(d) {
        if (dragging) return;
        box.innerHTML = '';
        var s = d.status, acc = s && s.account;
        var head = el('div', 'st-head');
        head.appendChild(el('h1', null, 'Start'));
        head.appendChild(el('span', 'st-sub', 'Instance ' + hide(d.instance) + (d.ageSec >= 0 ? ' · status ' + dur(d.ageSec) + ' old' : '')));
        var cb = el('input'); cb.type = 'checkbox'; cb.checked = masked();
        cb.addEventListener('change', function () { if (window.tbotHideData) window.tbotHideData.set(cb.checked); render(last); });
        var lbl = el('label', 'st-mask'); lbl.appendChild(cb); lbl.appendChild(document.createTextNode(' Hide data')); head.appendChild(lbl);
        var rb = el('button', 'cfg-btn sm', 'Reset layout'); rb.type = 'button'; rb.style.marginLeft = '10px'; rb.title = 'Restore the default tile order and widths';
        rb.addEventListener('click', resetLayout); head.appendChild(rb);
        var lk = el('button', 'cfg-btn sm', isLocked() ? 'Unlock layout' : 'Lock layout'); lk.type = 'button'; lk.style.marginLeft = '10px';
        lk.title = 'Lock: tiles can no longer be moved or resized by accident';
        lk.addEventListener('click', function () { setPref('lock', isLocked() ? '0' : '1'); render(last); }); head.appendChild(lk);
        var dl = el('label', 'st-mask'); dl.style.marginLeft = '10px'; dl.title = 'Smaller tiles move up to fill gaps';
        var dc = el('input'); dc.type = 'checkbox'; dc.checked = getPref('dense', '1') !== '0';
        dc.addEventListener('change', function () { setPref('dense', dc.checked ? '1' : '0'); render(last); });
        dl.appendChild(dc); dl.appendChild(document.createTextNode(' Auto-fill gaps')); head.appendChild(dl);
        var sl = el('label', 'st-mask'); sl.style.marginLeft = '10px'; sl.title = 'Tile background opacity';
        var rg = el('input'); rg.type = 'range'; rg.min = '15'; rg.max = '100'; rg.step = '5'; rg.value = String(readAlpha());
        rg.addEventListener('input', function () { setPref('alpha', rg.value); applyAlpha(); });
        sl.appendChild(document.createTextNode('Opacity ')); sl.appendChild(rg); head.appendChild(sl);
        applyAlpha();
        box.appendChild(head);

        var notes = [];
        var cc = d.configCheck || [];
        var ccStart = cc.length ? String(cc[0].start || 'nostart') : '';
        var ccKey = 'tbot.doctor.dismissed.' + instance;
        var ccHidden = false;
        try { ccHidden = ccStart !== '' && localStorage.getItem(ccKey) === ccStart; } catch (e) { ccHidden = false; }
        if (cc.length && !ccHidden) {
            var ccBox = el('div', 'st-note ' + (cc.some(function (x) { return x.severity === 'error'; }) ? 'bad' : (cc.some(function (x) { return x.severity === 'warn'; }) ? 'warn' : '')));
            cc.slice(0, 8).forEach(function (x) {
                var line = el('div', null);
                line.appendChild(el('b', null, x.severity === 'tip' ? 'Suggestion' : 'Config check'));
                if (x.severity === 'tip') {
                    line.appendChild(document.createTextNode(' - '));
                    line.appendChild(el('span', null, x.path));
                    line.appendChild(document.createTextNode(': '));
                } else {
                    line.appendChild(document.createTextNode(' - ' + x.path + ': '));
                }
                line.appendChild(el('span', null, x.message));
                if (x.severity === 'tip' && x.key && box.dataset.known) {
                    var kl = el('label', null); kl.style.marginLeft = '10px'; kl.style.whiteSpace = 'nowrap';
                    var kc = document.createElement('input'); kc.type = 'checkbox'; kc.style.marginRight = '4px';
                    kc.addEventListener('change', function () {
                        kc.disabled = true;
                        fetch(box.dataset.known + '?instance=' + encodeURIComponent(instance) + '&key=' + encodeURIComponent(x.key) + '&known=true', { method: 'POST' })
                            .then(function (r) { return r.json(); })
                            .then(function (r) { if (r.success) { line.remove(); if (!ccBox.querySelector('b')) ccBox.remove(); } else { kc.disabled = false; kc.checked = false; } })
                            .catch(function () { kc.disabled = false; kc.checked = false; });
                    });
                    kl.appendChild(kc); kl.appendChild(el('span', null, 'Known'));
                    line.appendChild(kl);
                }
                ccBox.appendChild(line);
            });
            if (cc.length > 8) ccBox.appendChild(el('div', null, (cc.length - 8) + ' more, see the log.'));
            var ccX = el('button', 'cfg-btn sm', 'Dismiss'); ccX.type = 'button'; ccX.style.marginTop = '6px';
            ccX.title = 'Hides these notes until the bot is restarted';
            ccX.addEventListener('click', function () {
                try { if (ccStart !== '') localStorage.setItem(ccKey, ccStart); } catch (e) { }
                ccBox.remove();
            });
            ccBox.appendChild(ccX);
            box.appendChild(ccBox);
        }
        if (d.configKnown > 0 && box.dataset.known) {
            var kn = el('div', 'st-mut', null);
            kn.appendChild(el('span', null, 'Suggestions marked as known (hidden):'));
            kn.appendChild(document.createTextNode(' ' + d.configKnown + ' '));
            var ka = el('a', 'st-link', 'Show again'); ka.href = '#';
            ka.addEventListener('click', function (ev) {
                ev.preventDefault();
                fetch(box.dataset.known + '?instance=' + encodeURIComponent(instance), { method: 'POST' }).then(function () { location.reload(); });
            });
            kn.appendChild(ka); box.appendChild(kn);
        }
        if (d.legacy) notes.push(['bad', 'Old configuration detected: only Defender and SleepMode run. Update the config with MigrateConfig (see config/README.md).']);
        if (s && s.attacks && s.attacks.length) notes.push(['bad', s.attacks.length + (s.attacksMore ? '+' : '') + ' incoming attack' + (s.attacks.length > 1 ? 's' : '') + ', next impact in ' + dur(s.attacks[0].inSec) + ' on ' + hide(s.attacks[0].dest)]);
        if (!s) notes.push(['warn', 'No status from the bot yet. The bot writes it every 30 seconds - is it running?']);
        else if (d.ageSec > 180) notes.push(['warn', 'The status is ' + dur(d.ageSec) + ' old. The bot may be stopped or stuck.']);
        if (d.modeOverride) notes.push(['warn', 'A special mode is active: AutoFarm and the other workers are switched off.']);
        notes.forEach(function (n) { box.appendChild(el('div', 'st-note ' + n[0], n[1])); });


        var c;
        var grid = el('div', 'st-grid' + (isLocked() ? ' st-locked' : '') + (getPref('dense', '1') !== '0' ? ' st-dense' : ''));
        box.appendChild(grid);

        if (s && s.attacks && s.attacks.length) {
            c = card('Incoming attacks', true); c.root.classList.add('st-attack'); c.root.style.borderColor = '#e66'; c.root.style.boxShadow = '0 0 0 1px #e66';
            s.attacks.forEach(function (x) {
                var mis = x.mission === 'GroupedAttack' ? 'ACS attack' : (x.mission === 'Destroy' ? 'Moon destruction' : (x.mission === 'MissileAttack' ? 'Missiles' : 'Attack'));
                c.body.appendChild(row(mis + ' from ' + hide(x.origin) + ' on ' + hide(x.dest), 'in ' + dur(x.inSec) + (x.at ? ' (' + x.at + ' bot time)' : ''), 'bad'));
            });
            if (s.attacksMore) c.body.appendChild(el('div', 'st-mut', '+ ' + s.attacksMore + ' more'));
            grid.appendChild(c.root);
        }

        c = card('Bot, version and workers'); c.root.classList.add('st-upd'); grid.appendChild(c.root);
        var two = el('div', 'st-two'), mergedLeft = el('div', 'st-upd-left'), mergedRight = el('div', 'st-upd-right');
        two.appendChild(mergedLeft); two.appendChild(mergedRight); c.body.appendChild(two);
        renderUpdate();

        c = card('Account and time'); grid.appendChild(c.root);
        if (acc) {
            var nm = el('div', 'st-big', hide(acc.name || '-')); c.body.appendChild(nm);
            c.body.appendChild(el('div', 'st-mut', (acc.universe ? hide(acc.universe) + ' · ' : '') + (acc.playerClass && acc.playerClass !== 'NoClass' ? acc.playerClass : 'no class')));
            c.body.appendChild(row('Rank', num(acc.rank) + (acc.total ? ' / ' + num(acc.total) : '')));
            c.body.appendChild(row('Points', compact(acc.points)));
            c.body.appendChild(row('Planets / moons', acc.planets + ' / ' + acc.moons));
            c.body.appendChild(row('Dark matter', (acc.darkmatter === null || acc.darkmatter === undefined) ? 'No data yet' : num(acc.darkmatter)));
            var discoveryOn = (d.workers || []).some(function (w) { return w.name === 'AutoDiscovery' && w.active; });
            if (acc.artifacts !== null && acc.artifacts !== undefined) c.body.appendChild(row('Artifacts', num(acc.artifacts) + (acc.artifactsLimit ? ' / ' + num(acc.artifactsLimit) : '')));
            else if (discoveryOn) c.body.appendChild(row('Artifacts', 'No data yet'));
        } else c.body.appendChild(el('div', 'st-mut', 'No account data yet.'));

        if (s && s.clock) {
            var th = el('div', 'st-sub2', 'Time'); c.body.appendChild(th);
            var tz = window.tbotTime ? window.tbotTime(s.clock) : null;
            if (tz) {
                c.body.appendChild(row('Bot time (game server)', tz.game + ' (' + tz.gameZone + ')'));
                c.body.appendChild(row('Your browser time', tz.browser + ' (' + tz.browserZone + ')'));
                c.body.appendChild(el('div', 'st-mut', tz.diffText));
                c.body.appendChild(el('div', 'st-mut', 'Sleep times, time windows and all schedules in the config use the bot time.'));
            }
        }

        c = card('Resources (all planets and moons)', true); grid.appendChild(c.root);
        if (acc) {
            var ph = acc.prodPerHour;
            var tb = el('table', 'st-restab');
            var hd = el('tr'); hd.appendChild(el('th', null, ''));
            ['Now', '1h', '6h', '12h', '18h', '24h'].forEach(function (h) { hd.appendChild(el('th', null, h)); });
            tb.appendChild(hd);
            [['Metal', acc.metal, ph && ph.metal], ['Crystal', acc.crystal, ph && ph.crystal], ['Deuterium', acc.deuterium, ph && ph.deuterium]].forEach(function (x) {
                var tr = el('tr'); tr.appendChild(el('td', 'st-mut', x[0]));
                var none = x[1] === null || x[1] === undefined;
                var cur = el('td', 'st-now', none ? '…' : compact(x[1])); if (!none) cur.title = num(x[1]); tr.appendChild(cur);
                [1, 6, 12, 18, 24].forEach(function (h) {
                    var td = el('td', null, (x[2] === null || x[2] === undefined) ? '…' : '+' + compact(x[2] * h));
                    if (x[2] !== null && x[2] !== undefined) td.title = num(x[2] * h);
                    tr.appendChild(td);
                });
                tb.appendChild(tr);
            });
            c.body.appendChild(tb);
            c.body.appendChild(el('div', 'st-mut', ph ? 'Yield = current hourly mine production of ' + ph.planets + ' planets x hours (before storage limits and raids).' : 'No production data yet. Press "Refresh now" or wait until the workers have read it.'));
            c.body.appendChild(refreshBar(acc.dataAge));
        } else c.body.appendChild(el('div', 'st-mut', 'No resource data yet.'));

        var gi = s && s.globalItems, evs = d.boosterStock && d.boosterStock.events;
        var passedG = gi && gi.fetchedMs ? Math.max(0, (Date.now() - gi.fetchedMs) / 1000) : 0;
        var presentKeys = {};
        var itemEntries = [];
        ((gi && gi.items) || []).forEach(function (it) {
            var left = Math.max(0, (it.remainingSec || 0) - passedG), key = 'item:' + (it.name || '?');
            presentKeys[key] = it.name || '?';
            itemEntries.push(itemTile(key, it.img, it.name || '?', left > 0 ? dur(left) : 'expired', left > 0 && left < 7200, (it.name || '') + (it.totalSec ? ' (total ' + dur(it.totalSec) + ')' : '')));
        });
        (evs || []).forEach(function (e) {
            var key = 'event:' + e.name;
            presentKeys[key] = 'Event: ' + e.name;
            itemEntries.push(itemTile(key, null, 'Event: ' + e.name + ' x' + e.amount, e.expiresInSec === null || e.expiresInSec === undefined ? 'expiry unknown' : dur(e.expiresInSec), false, e.name));
        });
        var known = readKnownItems(), changedKnown = false;
        Object.keys(presentKeys).forEach(function (k) { if (!known[k]) { known[k] = presentKeys[k]; changedKnown = true; } });
        if (changedKnown) saveKnownItems(known);
        Object.keys(known).forEach(function (k) { if (!presentKeys[k]) itemEntries.push(itemTile(k, null, known[k], '', false, known[k] + ' (not active right now)', true)); });
        if (itemEntries.length) {
            c = card('Global items (active on all planets)'); grid.appendChild(c.root);
            var itemBox = el('div', 'st-items'); c.body.appendChild(itemBox);
            itemEntries.forEach(function (t) { itemBox.appendChild(t); });
        }

        c = card('Fleet stock (ships on all planets and moons)', true); grid.appendChild(c.root);
        var fst = s && s.fleetStock;
        if (fst && fst.ships && fst.ships.length) {
            var shr = el('div', 'st-res');
            fst.ships.forEach(function (x) {
                var w = el('div'); w.appendChild(el('div', 'st-mut', camelWords(x.type))); w.appendChild(el('div', 'st-big', compact(x.count))); w.title = num(x.count); shr.appendChild(w);
            });
            c.body.appendChild(shr);
            c.body.appendChild(row('Fleet points', compact(fst.points)));
            c.body.appendChild(row('Celestials with ship data', fst.celestials + ' / ' + fst.of + (fst.celestials < fst.of ? ' (loading in the background…)' : '')));
        } else c.body.appendChild(el('div', 'st-mut', 'No ship data yet.'));

        c = card('Fleets sent'); grid.appendChild(c.root);
        var fcs = (s && s.fleetCounts) || [];
        if (fcs.length) {
            var ft = el('table', 'st-restab');
            var fh = el('tr'); ['', 'Today', '7 days', '30 days'].forEach(function (h) { fh.appendChild(el('th', null, h)); }); ft.appendChild(fh);
            var tot = [0, 0, 0];
            fcs.forEach(function (x) {
                var tr = el('tr'); tr.appendChild(el('td', 'st-mut', x.worker + (x.mission ? ' - ' + x.mission : '')));
                tr.firstChild.style.textAlign = 'left';
                [x.today, x.week, x.month].forEach(function (v, i) { tot[i] += v; tr.appendChild(el('td', null, num(v))); });
                ft.appendChild(tr);
            });
            var tt = el('tr'); tt.appendChild(el('td', 'st-mut', 'Total')); tt.firstChild.style.textAlign = 'left';
            tot.forEach(function (v) { var td = el('td', 'st-now', num(v)); tt.appendChild(td); });
            ft.appendChild(tt);
            c.body.appendChild(ft);
            c.body.appendChild(el('div', 'st-mut', 'Counted when a fleet was sent successfully (per worker and mission). Fleetsave and recall are counted under the worker that triggered them.'));
        } else c.body.appendChild(el('div', 'st-mut', 'No fleets sent yet since the counter started.'));

        c = card('Lifeform bonuses (expeditions)'); grid.appendChild(c.root);
        var lf = lfSnap && lfSnap.success ? lfSnap.data : null;
        if (lf) {
            var pc = function (v) { v = Number(v) || 0; return v ? (Math.round(v * 100000) / 1000) + ' %' : '0 %'; };
            var ex = lf.expeditions || {}, eb = lf.expeditionBonuses || {}, cg = lf.cargo || {}, sg = lf.suggestion || {};
            c.body.appendChild(row('Resources found', pc(ex.Resources)));
            Object.keys(eb).forEach(function (k) { if (k !== 'ResultBooster') c.body.appendChild(row('Bonus ' + k, pc(eb[k]))); });
            c.body.appendChild(row('Large cargo capacity', pc(cg.LargeCargo)));
            c.body.appendChild(row('Discoverer class bonus', pc(lf.discovererClass)));
            var tiers = [[10000, 40000], [100000, 500000], [1000000, 1200000], [5000000, 1800000], [25000000, 2400000], [50000000, 3000000], [75000000, 3600000], [100000000, 4200000]];
            var cap = 5000000;
            for (var ti = 0; ti < tiers.length; ti++) { if (lf.topScore < tiers[ti][0]) { cap = tiers[ti][1]; break; } }
            cap = lf.playerClass === 'Discoverer' ? cap * (lf.ecoSpeed || 1) * 3 : cap * 2;
            var resB = Number(ex.Resources) || 0;
            var findKey = function (re) { var ks = Object.keys(eb).filter(function (k) { return re.test(k); }); return ks.length ? ks[0] : null; };
            var shipKey = findKey(/ship/i), dmKey = findKey(/dark|matter|^dm/i);
            c.body.appendChild(row('Max find: metal', num(Math.ceil(cap * (1 + resB)))));
            c.body.appendChild(row('Max find: crystal', num(Math.ceil(cap * (1 + resB) / 2))));
            c.body.appendChild(row('Max find: deuterium', num(Math.ceil(cap * (1 + resB) / 3))));
            if (shipKey) c.body.appendChild(row('Max find: ships (value)', num(Math.ceil(cap * (1 + (Number(eb[shipKey]) || 0)) / 2))));
            if (dmKey) {
                var dm = 1 + (Number(eb[dmKey]) || 0);
                c.body.appendChild(row('Max find: dark matter', num(Math.floor(1800 * dm))));
            }
            if (!shipKey || !dmKey) c.body.appendChild(el('div', 'st-mut', 'Ship / dark matter finds: bonus not reported by the game under a known name yet (see the "Bonus ..." rows).'));
            if (sg.largeCargoPerExpedition > 0) c.body.appendChild(row('Large cargos per expedition', num(sg.largeCargoPerExpedition) + ' (' + num(sg.capacityPerLargeCargo) + ' each)'));
            var xf = lf.expoFinds;
            if (xf && xf.Reports > 0) {
                var both = function (r, s2) { return compact((r || 0) + (s2 || 0)) + ' / day'; };
                c.body.appendChild(row('Expedition finds, 7-day average', both(xf.Avg7ResDeut, xf.Avg7ShipsDeut)));
                c.body.appendChild(row('Expedition finds, 30-day average', both(xf.Avg30ResDeut, xf.Avg30ShipsDeut)));
                c.body.appendChild(row('Expedition finds, best 7 of 60 days', both(xf.Best7Of60ResDeut, xf.Best7Of60ShipsDeut)));
                c.body.appendChild(el('div', 'st-mut', 'From ' + num(xf.Reports) + ' saved reports (' + num(xf.DaysWithData) + ' days). In deuterium equivalent (3:2:1), resources plus found ships at build value.'));
            }
            c.body.appendChild(el('div', 'st-mut', 'As of ' + (lf.updatedUtc ? new Date(lf.updatedUtc).toLocaleString() : '-') + '. Details: Expeditions block.'));
        } else c.body.appendChild(el('div', 'st-mut', 'No data yet - the Expeditions worker writes it on its next run.'));

        c = { body: mergedRight };
        mergedRight.appendChild(el('div', 'st-sub2 first', 'Workers'));
        var wlist = el('div', 'st-wlist'); mergedRight.appendChild(wlist);
        var wr = (s && s.workerRuns) || [], passedW = s && s.updatedMs ? Math.max(0, (Date.now() - s.updatedMs) / 1000) : 0;
        var FEAT = { Expeditions: ['Expeditions'], AutoFarm: ['AutoFarm'], FastFarm: ['FastFarm'], Defender: ['Defender'], SleepMode: ['SleepMode'], AutoHarvest: ['Harvest'], AutoColonize: ['Colonize'], AutoDiscovery: ['AutoDiscovery'], AutoBidAuction: ['AutoBidAuction'], AutoFleetJumpGate: ['BrainAutoFleetJumpGate'] };
        var runText = function (w) { return w.busy ? 'running now' : (w.nextInSec === null || w.nextInSec === undefined ? '' : 'in ' + dur(Math.max(0, w.nextInSec - passedW))); };
        var runByName = function (n) { return wr.filter(function (w) { return w.name === n; })[0]; };
        (d.workers || []).forEach(function (w) {
            var r = el('div', 'st-row'); var l = el('a', 'st-wk'); l.href = blockLink(w.block || w.name);
            var on = w.active && !w.overridden;
            l.appendChild(el('span', 'dot ' + (on ? 'ok' : (w.overridden && w.active ? 'bad' : 'off'))));
            l.appendChild(document.createTextNode(w.name === 'SleepMode' ? 'Sleep' : w.name));
            r.appendChild(l);
            var info = '';
            if (w.overridden && w.active) info = 'off (override)';
            else if (on && FEAT[w.name]) {
                var run = FEAT[w.name].map(runByName).filter(Boolean)[0];
                if (run) info = runText(run);
            }
            if (w.name === 'Brain') r.className += ' keep';
            var inf = el('b', info === 'running now' ? 'ok' : 'st-mut', info); r.appendChild(inf);
            wlist.appendChild(r);
            if (w.name === 'Brain' && on) {
                wr.filter(function (x) { return /^Brain/.test(x.name) && x.name !== 'BrainAutoFleetJumpGate'; }).forEach(function (x) {
                    var sr = el('div', 'st-row st-subrow'); sr.appendChild(el('span', 'st-lbl', camelWords(String(x.name).replace(/^Brain/, ''))));
                    var t = runText(x); sr.appendChild(el('b', t === 'running now' ? 'ok' : 'st-mut', t));
                    wlist.appendChild(sr);
                });
            }
        });

        var fc = card('Fleets (slots and running fleets)', true); c = fc; grid.appendChild(c.root);
        var sl = s && s.slots;
        if (sl) {
            var bg = el('div', 'st-big'); bg.appendChild(document.createTextNode(sl.inUse + ' ')); bg.appendChild(el('span', 'st-mut', '/ ' + sl.total + ' slots')); c.body.appendChild(bg);
            var bar = el('div', 'st-bar'); var fill = el('i'); fill.style.width = Math.min(100, sl.total ? sl.inUse * 100 / sl.total : 0) + '%'; bar.appendChild(fill); c.body.appendChild(bar);
            c.body.appendChild(row('Free', String(sl.free)));
            c.body.appendChild(row('Expeditions', sl.expInUse + ' / ' + sl.expTotal));
            if (s.slotUse) c.body.appendChild(row('Attacks (AutoFarm)', String(s.slotUse.autofarm)));
        } else c.body.appendChild(el('div', 'st-mut', 'No slot data yet.'));


        c.body.appendChild(el('div', 'st-sub2', 'Running fleets'));
        var frn = s && s.fleetsRunning;
        if (frn && frn.total > 0) {
            var passed = s.updatedMs ? Math.max(0, (Date.now() - s.updatedMs) / 1000) : 0;
            var bg2 = el('div', 'st-big'); bg2.appendChild(document.createTextNode(frn.total + ' ')); bg2.appendChild(el('span', 'st-mut', 'fleets on the way')); c.body.appendChild(bg2);
            (frn.byMission || []).forEach(function (m) { c.body.appendChild(row(camelWords(m.mission), m.outbound + ' \u2192   ' + m.back + ' \u2190')); });
            c.body.appendChild(el('div', 'st-mut', 'Next arrivals'));
            (frn.next || []).forEach(function (f) {
                var lbl = camelWords(f.mission) + (f.back ? ' \u2190 ' : ' \u2192 ') + hide(f.back ? f.origin : f.dest);
                c.body.appendChild(row(lbl, dur(f.inSec - passed)));
            });
            if (frn.more) c.body.appendChild(el('div', 'st-mut', '+' + frn.more + ' more'));
        } else if (frn) c.body.appendChild(el('div', 'st-mut', 'No fleets on the way.'));
        else c.body.appendChild(el('div', 'st-mut', 'No fleet data yet.'));

        c = card('Latest events', true); grid.appendChild(c.root);
        var ev = d.events || [];
        if (!ev.length) c.body.appendChild(el('div', 'st-mut', 'No log entries today yet.'));
        else {
            var log = el('div', 'st-log');
            ev.forEach(function (e) {
                var line = el('div', 'st-ev ' + e.type.toLowerCase());
                line.appendChild(el('em', null, e.time));
                line.appendChild(document.createTextNode((e.sender ? e.sender + ': ' : '') + hide(e.message)));
                log.appendChild(line);
            });
            c.body.appendChild(log);
        }
        applyLayout(grid);
    }

    var dragging = false, dragTimer = null, dragEl = null;
    function orderKey() { return 'tbot.start.order.' + instance; }
    function readOrder() { try { var v = JSON.parse(localStorage.getItem(orderKey()) || '[]'); return Array.isArray(v) ? v : []; } catch (e) { return []; } }
    function saveOrder(grid) {
        try { localStorage.setItem(orderKey(), JSON.stringify(Array.prototype.map.call(grid.children, function (c) { return c.dataset.key; }).filter(Boolean))); } catch (e) {  }
    }
    function endDrag(grid) {
        if (!dragging) return;
        dragging = false;
        if (dragTimer) { clearTimeout(dragTimer); dragTimer = null; }
        if (dragEl) { dragEl.classList.remove('st-drag'); dragEl.draggable = false; dragEl = null; }
        saveOrder(grid);
        load();
    }
    var DEFAULT_W = { 'Bot, version and workers': 3, 'Account and time': 1, 'Lifeform bonuses (expeditions)': 2, 'Resources (all planets and moons)': 2, 'Fleets (slots and running fleets)': 2, 'Fleet stock (ships on all planets and moons)': 2, 'Latest events': 4, 'Global items (active on all planets)': 4 };
    function sizeKey() { return 'tbot.start.size.' + instance; }
    function readSizes() { try { var v = JSON.parse(localStorage.getItem(sizeKey()) || '{}'); return v && typeof v === 'object' ? v : {}; } catch (e) { return {}; } }
    function setWidth(c, w) { c.className = c.className.replace(/\bw[1-4]\b/g, '').trim() + ' w' + w; c.dataset.w = String(w); }
    function applyLayout(grid) {
        var saved = readOrder(), sizes = readSizes(), cards = Array.prototype.slice.call(grid.children);
        var idx = function (c) { var i = saved.indexOf(c.dataset.key); return i < 0 ? 100000 + cards.indexOf(c) : i; };
        cards.filter(function (c) { return !c.classList.contains('st-attack'); }).sort(function (a, b) { return idx(a) - idx(b); }).forEach(function (c) { grid.appendChild(c); });
        cards.filter(function (c) { return c.classList.contains('st-attack'); }).reverse().forEach(function (c) { grid.insertBefore(c, grid.firstChild); });

        cards.forEach(function (c) {
            if (c.classList.contains('st-attack')) { setWidth(c, 4); return; }
            var key = c.dataset.key, w = Number(sizes[key]) || DEFAULT_W[key] || 1;
            setWidth(c, w);
            if (isLocked()) return;
            var isItem = c.classList.contains('st-item');
            var h = isItem ? c : c.querySelector('h2');
            if (!h) return;
            if (!isItem) h.title = 'Drag to move this tile';
            var sz = el('span', 'st-size'); sz.title = 'Tile width (columns)';
            [1, 2, 3, 4].forEach(function (n) {
                var bt = el('button', n === w ? 'on' : '', String(n)); bt.type = 'button';
                bt.addEventListener('mousedown', function (e) { e.stopPropagation(); });
                bt.addEventListener('click', function (e) {
                    e.stopPropagation();
                    setWidth(c, n);
                    Array.prototype.forEach.call(sz.children, function (x) { x.className = x.textContent === String(n) ? 'on' : ''; });
                    var cur = readSizes(); cur[key] = n;
                    try { localStorage.setItem(sizeKey(), JSON.stringify(cur)); } catch (x) {  }
                });
                sz.appendChild(bt);
            });
            if (!isItem) h.appendChild(sz);
            h.addEventListener('mousedown', function () { c.draggable = true; });
            h.addEventListener('mouseup', function () { if (!dragging) c.draggable = false; });
            c.addEventListener('dragstart', function (e) {
                dragging = true; dragEl = c; c.classList.add('st-drag');
                try { e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/plain', c.dataset.key); } catch (x) { }
                dragTimer = setTimeout(function () { endDrag(grid); }, 30000);
            });
            c.addEventListener('dragend', function () { endDrag(grid); });
        });
        grid.addEventListener('dragover', function (e) {
            if (!dragging || !dragEl) return;
            e.preventDefault();
            var t = e.target.closest ? e.target.closest('.st-card') : null;
            if (!t || t === dragEl || t.parentNode !== grid || t.classList.contains('st-attack')) return;
            var r = t.getBoundingClientRect();
            var after = r.width > grid.clientWidth * 0.55 ? e.clientY > r.top + r.height / 2 : e.clientX > r.left + r.width / 2;
            grid.insertBefore(dragEl, after ? t.nextSibling : t);
        });
        grid.addEventListener('drop', function (e) { if (dragging) e.preventDefault(); });
    }
    function resetLayout() { try { localStorage.removeItem(orderKey()); localStorage.removeItem(sizeKey()); localStorage.removeItem(knownKey()); } catch (e) { } if (last) render(last); }

    function post(u) { return fetch(u, { method: 'POST' }).then(function (r) { return r.json(); }); }
    function renderUpdate() {
        var b = box.querySelector('.st-upd-left');
        if (!b) return;
        b.innerHTML = '';
        if (!upd && !last) return;
        var info = (upd && upd.info) || {};
        var rootCard = b.closest ? b.closest('.st-card') : null;
        if (rootCard) rootCard.classList.toggle('avail', !!info.available);
        var s = last && last.status;
        var state = !s ? 'Unknown' : (s.sleeping ? (s.mode === 'GhostSleep' ? 'Ghost sleep' : 'Sleeping') : 'Running');
        var big = el('div', 'st-big'); big.appendChild(el('span', 'dot ' + (!s ? 'off' : (s.sleeping ? 'warn' : 'ok')))); big.appendChild(document.createTextNode(state));
        b.appendChild(big);
        if (s) {
            if (s.sleeping && s.wakeInSec !== null && s.wakeInSec !== undefined) b.appendChild(row('Wakes up in', dur(s.wakeInSec)));
            else if (!s.sleeping && s.sleepInSec !== null && s.sleepInSec !== undefined) b.appendChild(row('Sleep starts in', dur(s.sleepInSec)));
            if (s.recalls && s.recalls.length) b.appendChild(row('Pending recalls', String(s.recalls.length + (s.more || 0))));
            if (s.modeOverride) b.appendChild(row('Mode', s.modeOverride.name + ' (override)', 'bad'));
        }
        if (!upd) return;
        var line = el('div', 'st-updline');
        line.appendChild(el('span', 'st-lbl', 'Installed'));
        line.appendChild(el('b', null, info.current || '-'));
        b.appendChild(line);

        var res = null; try { res = upd.last ? JSON.parse(upd.last) : null; } catch (e) { res = null; }
        if (res) {
            var r = el('div', 'st-note ' + (res.ok ? 'ok' : 'bad'), (res.ok ? 'Last update installed: ' : 'Last update failed: ') + (res.message || '') + (res.from && res.to ? ' (' + res.from + ' → ' + res.to + ')' : ''));
            var x = el('button', 'cfg-btn sm', 'Dismiss'); x.type = 'button'; x.style.marginLeft = '10px';
            x.addEventListener('click', function () { post(box.dataset.upddismiss).then(function () { upd.last = null; renderUpdate(); }); });
            r.appendChild(x); b.appendChild(r);
        }
        if (info.error) {
            b.appendChild(el('div', 'st-mut', 'Update check failed: ' + info.error));
        } else if (info.available) {
            var l2 = el('div', 'st-updline'); l2.appendChild(el('span', 'st-lbl', 'New version')); l2.appendChild(el('b', 'new', info.latest + (info.prerelease ? ' (pre-release)' : ''))); b.appendChild(l2);
            if (info.notes) b.appendChild(el('pre', 'st-notes', info.notes));
            var act = el('div', 'st-actions');
            if (info.canInstall) {
                var inst = el('button', 'cfg-btn p', updBusy ? 'Installing…' : 'Install update'); inst.type = 'button'; inst.disabled = updBusy;
                inst.addEventListener('click', function () { startUpdate(info); });
                act.appendChild(inst);
            } else if (info.installBlock) act.appendChild(el('span', 'st-mut', info.installBlock));
            if (info.url && /^https:\/\//.test(info.url)) { var a1 = el('a', 'st-link', 'Release page'); a1.href = info.url; a1.target = '_blank'; a1.rel = 'noopener'; act.appendChild(a1); }
            b.appendChild(act);
        } else if (!info.latest) {
            b.appendChild(el('div', 'st-mut', 'No release information yet.'));
        } else {
            b.appendChild(el('div', 'st-mut', info.current === 'dev' ? 'Development build - latest release is ' + info.latest + '.' : 'You are on the latest version (' + info.latest + ').'));
        }
        var chk = el('button', 'cfg-btn sm', 'Check now'); chk.type = 'button'; chk.style.marginTop = '8px';
        chk.addEventListener('click', function () { loadUpdate(true); });
        b.appendChild(chk);
    }
    function loadUpdate(force) {
        fetch(box.dataset.upd + (force ? '?force=true' : ''), { cache: 'no-store' })
            .then(function (r) { return r.json(); })
            .then(function (d) { upd = d; renderUpdate(); })
            .catch(function () {  });
    }
    function startUpdate(info) {
        if (!window.confirm('Install ' + info.latest + ' now?\n\nThe bot stops, the files are replaced (config, data and logins stay) and the bot restarts. This takes about a minute.\nIf the new version does not start, the old one is restored automatically.')) return;
        updBusy = true; renderUpdate();
        post(box.dataset.updstart).then(function (r) {
            if (!r.success) { updBusy = false; renderUpdate(); alert('Update could not be started: ' + r.error); return; }
            box.innerHTML = '';
            box.appendChild(el('div', 'st-note warn', r.message + ' This page reloads by itself when the bot is back.'));
            var tries = 0;
            var timer = setInterval(function () {
                tries++;
                fetch(url + '?instance=' + encodeURIComponent(instance), { cache: 'no-store' }).then(function (x) { return x.ok ? x.json() : null; }).then(function (d) {
                    if (d && tries > 6) { clearInterval(timer); window.location.reload(); }
                }).catch(function () {  });
                if (tries > 120) { clearInterval(timer); box.appendChild(el('div', 'st-note bad', 'The bot is not back after 10 minutes. Check the folder update\\updater.log.')); }
            }, 5000);
        }).catch(function (e) { updBusy = false; renderUpdate(); alert('Update could not be started: ' + e); });
    }

    function loadLf() {
        if (!main.dataset.lf || Date.now() - lfAt < 300000) return;
        lfAt = Date.now();
        fetch(main.dataset.lf + '?instance=' + encodeURIComponent(instance), { cache: 'no-store' })
            .then(function (r) { return r.json(); })
            .then(function (r) { lfSnap = r; if (last) render(last); })
            .catch(function () {  });
    }

    function cacheKey() { return 'tbot.start.cache.' + instance; }
    function showCached() {
        try {
            var raw = localStorage.getItem(cacheKey());
            if (!raw) return;
            var c = JSON.parse(raw);
            if (c && c.data && !c.data.error) { last = c.data; render(c.data); box.classList.add('st-cached'); }
        } catch (e) {  }
    }
    function saveCache(d) { try { localStorage.setItem(cacheKey(), JSON.stringify({ at: Date.now(), data: d })); } catch (e) {  } }
    function load() {
        if (dragging) return;
        fetch(url + '?instance=' + encodeURIComponent(instance), { cache: 'no-store' })
            .then(function (r) { return r.json(); })
            .then(function (d) { if (d.error) { box.textContent = 'Could not read the data: ' + d.error; return; } last = d; box.classList.remove('st-cached'); render(d); saveCache(d); loadLf(); })
            .catch(function (e) { if (!last) box.textContent = 'Could not read the data: ' + e; });
    }
    showCached();
    load();
    loadUpdate(false);
    setInterval(load, 15000);
})();
