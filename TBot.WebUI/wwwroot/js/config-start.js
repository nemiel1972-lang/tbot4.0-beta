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
    function row(label, value, cls) {
        var r = el('div', 'st-row');
        r.appendChild(el('span', 'st-lbl', label));
        var v = el('b', cls || '', value); r.appendChild(v);
        return r;
    }
    function card(title, wide) {
        var c = el('section', 'st-card' + (wide ? ' wide' : ''));
        c.appendChild(el('h2', null, title));
        var b = el('div', 'st-body'); c.appendChild(b);
        return { root: c, body: b };
    }
    function blockLink(name) {
        return base + '?instance=' + encodeURIComponent(instance) + '&block=' + encodeURIComponent(name);
    }

    function render(d) {
        box.innerHTML = '';
        var s = d.status, acc = s && s.account;
        var head = el('div', 'st-head');
        head.appendChild(el('h1', null, 'Start'));
        head.appendChild(el('span', 'st-sub', 'Instance ' + hide(d.instance) + (d.ageSec >= 0 ? ' · status ' + dur(d.ageSec) + ' old' : '')));
        var cb = el('input'); cb.type = 'checkbox'; cb.checked = masked();
        cb.addEventListener('change', function () { if (window.tbotHideData) window.tbotHideData.set(cb.checked); render(last); });
        var lbl = el('label', 'st-mask'); lbl.appendChild(cb); lbl.appendChild(document.createTextNode(' Hide data')); head.appendChild(lbl);
        box.appendChild(head);

        var notes = [];
        if (d.legacy) notes.push(['bad', 'Old configuration detected: only Defender and SleepMode run. Update the config with MigrateConfig (see config/README.md).']);
        if (s && s.attacks && s.attacks.length) notes.push(['bad', s.attacks.length + (s.attacksMore ? '+' : '') + ' incoming attack' + (s.attacks.length > 1 ? 's' : '') + ', next impact in ' + dur(s.attacks[0].inSec) + ' on ' + hide(s.attacks[0].dest)]);
        if (!s) notes.push(['warn', 'No status from the bot yet. The bot writes it every 30 seconds - is it running?']);
        else if (d.ageSec > 180) notes.push(['warn', 'The status is ' + dur(d.ageSec) + ' old. The bot may be stopped or stuck.']);
        if (d.modeOverride) notes.push(['warn', 'A special mode is active: AutoFarm and the other workers are switched off.']);
        notes.forEach(function (n) { box.appendChild(el('div', 'st-note ' + n[0], n[1])); });

        renderUpdate();

        var grid = el('div', 'st-grid');
        box.appendChild(grid);

        var c = card('Bot status'); grid.appendChild(c.root);
        var state = !s ? 'Unknown' : (s.sleeping ? (s.mode === 'GhostSleep' ? 'Ghost sleep' : 'Sleeping') : 'Running');
        var big = el('div', 'st-big'); big.appendChild(el('span', 'dot ' + (!s ? 'off' : (s.sleeping ? 'warn' : 'ok')))); big.appendChild(document.createTextNode(state));
        c.body.appendChild(big);
        if (s) {
            if (s.sleeping && s.wakeInSec !== null && s.wakeInSec !== undefined) c.body.appendChild(row('Wakes up in', dur(s.wakeInSec)));
            else if (!s.sleeping && s.sleepInSec !== null && s.sleepInSec !== undefined) c.body.appendChild(row('Sleep starts in', dur(s.sleepInSec)));
            if (s.recalls && s.recalls.length) c.body.appendChild(row('Pending recalls', String(s.recalls.length + (s.more || 0))));
            if (s.modeOverride) c.body.appendChild(row('Mode', s.modeOverride.name + ' (override)', 'bad'));
        }

        if (s && s.clock) {
            c = card('Time'); grid.appendChild(c.root);
            var tz = window.tbotTime ? window.tbotTime(s.clock) : null;
            if (tz) {
                c.body.appendChild(row('Bot time (game server)', tz.game + ' (' + tz.gameZone + ')'));
                c.body.appendChild(row('Your browser time', tz.browser + ' (' + tz.browserZone + ')'));
                c.body.appendChild(el('div', 'st-mut', tz.diffText));
                c.body.appendChild(el('div', 'st-mut', 'Sleep times, time windows and all schedules in the config use the bot time.'));
            }
        }

        c = card('Account'); grid.appendChild(c.root);
        if (acc) {
            var nm = el('div', 'st-big', hide(acc.name || '-')); c.body.appendChild(nm);
            c.body.appendChild(el('div', 'st-mut', (acc.universe ? hide(acc.universe) + ' · ' : '') + (acc.playerClass && acc.playerClass !== 'NoClass' ? acc.playerClass : 'no class')));
            c.body.appendChild(row('Rank', num(acc.rank) + (acc.total ? ' / ' + num(acc.total) : '')));
            c.body.appendChild(row('Points', compact(acc.points)));
            c.body.appendChild(row('Planets / moons', acc.planets + ' / ' + acc.moons));
            c.body.appendChild(row('Dark matter', (acc.darkmatter === null || acc.darkmatter === undefined) ? 'Calculating…' : num(acc.darkmatter)));
            var discoveryOn = (d.workers || []).some(function (w) { return w.name === 'AutoDiscovery' && w.active; });
            if (acc.artifacts !== null && acc.artifacts !== undefined) c.body.appendChild(row('Artifacts', num(acc.artifacts) + (acc.artifactsLimit ? ' / ' + num(acc.artifactsLimit) : '')));
            else if (discoveryOn) c.body.appendChild(row('Artifacts', 'Calculating…'));
        } else c.body.appendChild(el('div', 'st-mut', 'No account data yet.'));

        c = card('Fleets'); grid.appendChild(c.root);
        var sl = s && s.slots;
        if (sl) {
            var bg = el('div', 'st-big'); bg.appendChild(document.createTextNode(sl.inUse + ' ')); bg.appendChild(el('span', 'st-mut', '/ ' + sl.total + ' slots')); c.body.appendChild(bg);
            var bar = el('div', 'st-bar'); var fill = el('i'); fill.style.width = Math.min(100, sl.total ? sl.inUse * 100 / sl.total : 0) + '%'; bar.appendChild(fill); c.body.appendChild(bar);
            c.body.appendChild(row('Free', String(sl.free)));
            c.body.appendChild(row('Expeditions', sl.expInUse + ' / ' + sl.expTotal));
            if (s.slotUse) c.body.appendChild(row('Attacks (AutoFarm)', String(s.slotUse.autofarm)));
        } else c.body.appendChild(el('div', 'st-mut', 'No slot data yet.'));

        c = card('Resources (all planets and moons)', true); grid.appendChild(c.root);
        if (acc) {
            var rr = el('div', 'st-res');
            [['Metal', acc.metal], ['Crystal', acc.crystal], ['Deuterium', acc.deuterium]].forEach(function (x) {
                var none = x[1] === null || x[1] === undefined;
                var w = el('div'); w.appendChild(el('div', 'st-mut', x[0])); w.appendChild(el('div', 'st-big', none ? 'Calculating…' : compact(x[1]))); if (!none) w.title = num(x[1]); rr.appendChild(w);
            });
            c.body.appendChild(rr);
        } else c.body.appendChild(el('div', 'st-mut', 'No resource data yet.'));

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

        c = card('Workers'); c.root.classList.add('tall'); grid.appendChild(c.root);
        (d.workers || []).forEach(function (w) {
            var r = el('div', 'st-row'); var l = el('a', 'st-wk'); l.href = blockLink(w.name);
            l.appendChild(el('span', 'dot ' + (w.active && !w.overridden ? 'ok' : (w.overridden && w.active ? 'bad' : 'off'))));
            l.appendChild(document.createTextNode(w.name === 'SleepMode' ? 'Sleep' : w.name));
            r.appendChild(l);
            r.appendChild(el('span', 'st-mut', w.overridden && w.active ? 'off (override)' : (w.active ? 'on' : 'off')));
            c.body.appendChild(r);
        });

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
    }

    function post(u) { return fetch(u, { method: 'POST' }).then(function (r) { return r.json(); }); }
    function renderUpdate() {
        var holder = box.querySelector('.st-upd');
        if (holder) holder.remove();
        if (!upd) return;
        var info = upd.info || {}, c = el('section', 'st-card st-upd' + (info.available ? ' avail' : ''));
        c.appendChild(el('h2', null, 'Version'));
        var b = el('div', 'st-body'); c.appendChild(b);
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
        var anchor = box.querySelector('.st-grid');
        if (anchor) box.insertBefore(c, anchor); else box.appendChild(c);
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

    function load() {
        fetch(url + '?instance=' + encodeURIComponent(instance), { cache: 'no-store' })
            .then(function (r) { return r.json(); })
            .then(function (d) { if (d.error) { box.textContent = 'Could not read the data: ' + d.error; return; } last = d; render(d); loadLf(); })
            .catch(function (e) { if (!last) box.textContent = 'Could not read the data: ' + e; });
    }
    load();
    loadUpdate(false);
    setInterval(load, 15000);
})();
