(function () {
    'use strict';
    var main = document.getElementById('cfg-main');
    var root = document.getElementById('cfg-root');
    if (!main) { wireSearch(); return; }
    wireSearch();
    wireSwitches();

    var DRAFT_KEY = 'tbot.cfgdraft.' + main.dataset.instance;
    var SAVE_SCOPE = { TelegramMessenger: 'global' };
    var DRAFT_PAGE = { TelegramMessenger: 'Messenger', DiscordWebhook: 'Messenger', GhostSleep: 'SleepMode', Experimental: 'Testing' };
    function readDrafts() { try { return JSON.parse(sessionStorage.getItem(DRAFT_KEY) || '{}') || {}; } catch (e) { return {}; } }
    function writeDrafts(d) { try { if (Object.keys(d).length) sessionStorage.setItem(DRAFT_KEY, JSON.stringify(d)); else sessionStorage.removeItem(DRAFT_KEY); } catch (e) { } }
    function clearPendingActive(name) {
        var d = readDrafts();
        if (d.__active && d.__active[name] !== undefined) { delete d.__active[name]; if (!Object.keys(d.__active).length) delete d.__active; writeDrafts(d); }
        document.querySelectorAll('#cfg-blocks .sw.click').forEach(function (sw) { if (sw.dataset.block === name && sw.dataset.orig !== undefined) sw.classList.toggle('on', sw.dataset.orig === '1'); });
    }
    function clearDraft(name) { var d = readDrafts(); delete d[name]; writeDrafts(d); renderDraftBar(); }
    function postBlock(name, content) {
        var fd = new URLSearchParams(); fd.set('instance', main.dataset.instance); fd.set('block', name); fd.set('content', JSON.stringify(content)); if (SAVE_SCOPE[name]) fd.set('scope', SAVE_SCOPE[name]);
        return fetch(main.dataset.save, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: fd.toString() }).then(function (r) { return r.json(); });
    }
    function renderDraftBar() {
        var d = readDrafts(), act = d.__active || {}, names = Object.keys(d).filter(function (n) { return n !== '__active'; }), pages = {};
        names.forEach(function (n) { pages[DRAFT_PAGE[n] || n] = true; });
        Object.keys(act).forEach(function (n) { pages[n] = true; if (names.indexOf(n) < 0) names.push(n); });
        document.querySelectorAll('#cfg-blocks .sw.click').forEach(function (sw) { if (act[sw.dataset.block] !== undefined) sw.classList.toggle('on', !!act[sw.dataset.block]); });
        document.querySelectorAll('#cfg-blocks li').forEach(function (li) { li.classList.toggle('has-draft', !!pages[li.dataset.name]); });
        var bar = document.getElementById('cfg-draftbar');
        if (!names.length) { if (bar) bar.remove(); return; }
        if (!bar) { bar = document.createElement('div'); bar.id = 'cfg-draftbar'; bar.className = 'cfg-draftbar'; var pg = document.querySelector('.cfg-page'); if (pg) pg.insertBefore(bar, pg.firstChild); else document.body.insertBefore(bar, document.body.firstChild); }
        bar.innerHTML = '';
        var lbl = document.createElement('span'); lbl.className = 'lbl';
        lbl.textContent = '⚠ Not saved yet - ' + names.length + ' unsaved block' + (names.length > 1 ? 's' : '') + ': ' + names.join(', ');
        var bSave = document.createElement('button'); bSave.type = 'button'; bSave.className = 'cfg-btn p'; bSave.textContent = 'Save all';
        var bDisc = document.createElement('button'); bDisc.type = 'button'; bDisc.className = 'cfg-btn'; bDisc.textContent = 'Discard & reload';
        bSave.addEventListener('click', function () {
            bSave.disabled = true; bDisc.disabled = true;
            var cur = readDrafts(), act = cur.__active || {};
            var jobs = Object.keys(cur).filter(function (n) { return n !== '__active'; }).map(function (n) { return { n: n, run: function () { return postBlock(n, cur[n]); }, done: function () { clearDraft(n); } }; });
            Object.keys(act).forEach(function (n) {
                jobs.push({ n: n + ' (Active)', run: function () {
                    var fd = new URLSearchParams(); fd.set('instance', main.dataset.instance); fd.set('block', n); fd.set('active', act[n] ? 'true' : 'false');
                    return fetch(main.dataset.setactive, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: fd.toString() }).then(function (r) { return r.json(); });
                }, done: function () { var d2 = readDrafts(); if (d2.__active) { delete d2.__active[n]; if (!Object.keys(d2.__active).length) delete d2.__active; } writeDrafts(d2); renderDraftBar(); } });
            });
            jobs.reduce(function (p, job) {
                return p.then(function (ok) {
                    if (!ok) return false;
                    return job.run().then(function (r) {
                        if (!r.success) { alert('Save failed (' + job.n + '): ' + r.error); return false; }
                        job.done(); return true;
                    });
                });
            }, Promise.resolve(true)).then(function (ok) {
                if (ok) window.location.reload(); else { bSave.disabled = false; bDisc.disabled = false; }
            }).catch(function (e) { alert('Save failed: ' + e); bSave.disabled = false; bDisc.disabled = false; });
        });
        bDisc.addEventListener('click', function () {
            if (!window.confirm('Discard all unsaved changes in ' + names.length + ' block(s)?')) return;
            writeDrafts({}); window.location.reload();
        });
        bar.appendChild(lbl); bar.appendChild(bDisc); bar.appendChild(bSave);
    }
    renderDraftBar();
    if (!root) return;

    var instance = main.dataset.instance, block = main.dataset.block;
    var SENS_RE = /host|alliance|galaxy|system|position|coord|name|url|id$/i;
    var moreOpen = false, lfOpen = false, escOpen = false, openSecs = {};
    var mask = window.tbotHideData ? window.tbotHideData.get() : false;
    var sensNodes = [];
    function setMasked(n, on) {
        if (n.tagName === 'INPUT') {
            if (on && !n.dataset.m) { n.dataset.m = '1'; n.dataset.t = n.type; n.dataset.v = n.value; n.type = 'text'; n.value = '*****'; n.readOnly = true; }
            else if (!on && n.dataset.m) { n.type = n.dataset.t; n.value = n.dataset.v; n.readOnly = false; delete n.dataset.m; }
        } else {
            if (on && !n.dataset.m) { n.dataset.m = '1'; n.dataset.v = n.textContent; n.textContent = '*****'; }
            else if (!on && n.dataset.m) { n.textContent = n.dataset.v; delete n.dataset.m; }
        }
    }
    function sens(node) {
        sensNodes.push(node);
        return node;
    }
    var ALWAYS_MASK = /webhook|password|token|apikey|secret|^api$/i;
    function restoreSecrets(parsed, orig) {
        if (!parsed || !orig || typeof parsed !== 'object' || typeof orig !== 'object') return;
        Object.keys(parsed).forEach(function (k) {
            if (parsed[k] === '*****' && has(orig, k)) parsed[k] = orig[k];
            else restoreSecrets(parsed[k], orig[k]);
        });
    }
    function maskedCopy(o, key) {
        if (Array.isArray(o)) return o.map(function (x) { return maskedCopy(x, key); });
        if (o && typeof o === 'object') { var r = {}; Object.keys(o).forEach(function (k) { r[k] = maskedCopy(o[k], k); }); return r; }
        return key && (SENS_RE.test(key) || ALWAYS_MASK.test(key)) && o !== null && o !== '' ? '*****' : o;
    }
    function shown(o) { return JSON.stringify(mask ? maskedCopy(o) : maskedSecretsOnly(o), null, 2); }
    function maskedSecretsOnly(o, key) {
        if (Array.isArray(o)) return o.map(function (x) { return maskedSecretsOnly(x, key); });
        if (o && typeof o === 'object') { var r = {}; Object.keys(o).forEach(function (k) { r[k] = maskedSecretsOnly(o[k], k); }); return r; }
        return key && ALWAYS_MASK.test(key) && o !== null && o !== '' ? '*****' : o;
    }
    function applyMask() { sensNodes.forEach(function (n) { setMasked(n, mask); }); }
    var original = null, state = null, originalText = '', view = 'form', file = '', addedDefaults = [];

    var SHIPS = ['LightFighter', 'HeavyFighter', 'Cruiser', 'Battleship', 'Battlecruiser', 'Bomber', 'Destroyer', 'Reaper', 'Pathfinder', 'SmallCargo', 'LargeCargo', 'EspionageProbe'];
    var COMBAT = ['Reaper', 'Destroyer', 'Bomber', 'Battlecruiser', 'Battleship', 'Pathfinder', 'Cruiser', 'HeavyFighter', 'LightFighter'];
    var PRIMARY = ['LargeCargo', 'SmallCargo', 'Discoverer'];
    var TIME_RE = /^(\d{1,2}:\d{2}(:\d{2})?|WakeUp([+-]\d+)?|SleepStart([+-]\d+)?)?$/i;

    function el(tag, attrs, kids) {
        var e = document.createElement(tag);
        if (attrs) Object.keys(attrs).forEach(function (k) {
            if (k === 'class') e.className = attrs[k];
            else if (k === 'text') e.textContent = attrs[k];
            else if (k.indexOf('on') === 0) e.addEventListener(k.slice(2), attrs[k]);
            else if (attrs[k] !== null && attrs[k] !== undefined) e.setAttribute(k, attrs[k]);
        });
        (kids || []).forEach(function (c) { if (c) e.appendChild(typeof c === 'string' ? document.createTextNode(c) : c); });
        return e;
    }
    function clone(o) { return JSON.parse(JSON.stringify(o)); }
    function dirty() { return JSON.stringify(state) !== JSON.stringify(original); }

    var onSidebarActive = null;
    function wireSwitches() {
        document.querySelectorAll('#cfg-blocks .sw.click').forEach(function (sw) {
            sw.dataset.orig = sw.classList.contains('on') ? '1' : '0';
            sw.addEventListener('click', function (ev) {
                ev.preventDefault(); ev.stopPropagation();
                var name = sw.dataset.block, want = !sw.classList.contains('on');
                var d = readDrafts(), act = d.__active || {};
                if ((want ? '1' : '0') === sw.dataset.orig) delete act[name]; else act[name] = want;
                if (Object.keys(act).length) d.__active = act; else delete d.__active;
                writeDrafts(d);
                if (onSidebarActive) onSidebarActive(name, want);
                renderDraftBar();
            });
        });
    }
    function wireSearch() {
        var s = document.getElementById('cfg-search');
        if (!s) return;
        s.addEventListener('input', function () {
            var q = s.value.toLowerCase();
            document.querySelectorAll('#cfg-blocks li').forEach(function (li) {
                li.style.display = li.dataset.name.toLowerCase().indexOf(q) >= 0 ? '' : 'none';
            });
        });
    }

    var SCOPE = { TelegramMessenger: 'global' };
    function getBlock(name) {
        return fetch(main.dataset.get + '?instance=' + encodeURIComponent(instance) + '&block=' + encodeURIComponent(name) + (SCOPE[name] ? '&scope=' + SCOPE[name] : ''), { cache: 'no-store' }).then(function (r) { return r.json(); });
    }
    var COMBINED = block === 'SleepMode' ? ['SleepMode', 'GhostSleep'] : (block === 'Messenger' ? ['TelegramMessenger', 'DiscordWebhook'] : (block === 'Testing' ? ['Experimental', 'General', 'Brain'] : null));
    var COMBINED_TITLE = block === 'Messenger' ? 'Messenger' : (block === 'Testing' ? 'Testing' : 'Sleep / GhostSleep');
    var loading = COMBINED
        ? Promise.all(COMBINED.map(getBlock)).then(function (rs) {
            var data = {}, files = [], firstError = null;
            rs.forEach(function (r, i) {
                if (r.success) { data[COMBINED[i]] = JSON.parse(r.data); files.push(r.file); } else if (!firstError) firstError = r;
            });
            if (!Object.keys(data).length) return firstError;
            if (block === 'SleepMode' && !data.SleepMode) return firstError;
            return { success: true, data: JSON.stringify(data), file: files.join(' + ') };
        })
        : getBlock(block);
    loading
        .then(function (r) {
            if (!r.success) { root.innerHTML = ''; root.appendChild(el('div', { class: 'cfg-error', text: r.error })); return; }
            original = JSON.parse(r.data); state = clone(original); file = r.file;
            addedDefaults = (!COMBINED && r.added) || [];
            addedDefaults.forEach(function (p) {
                var parts = p.split('.').slice(1), o = original;
                for (var i = 0; i < parts.length - 1 && o; i++) o = o[parts[i]];
                if (o && typeof o === 'object') delete o[parts[parts.length - 1]];
            });
            originalText = JSON.stringify(original, null, 2);
            var dr = readDrafts();
            if (!COMBINED && dr.__active && dr.__active[block] !== undefined && dr[block] === undefined && typeof state.Active === 'boolean') state.Active = !!dr.__active[block];
            onSidebarActive = function (name, v) { if (!COMBINED && name === block && state && typeof state.Active === 'boolean') { state.Active = v; render(); } };
            (COMBINED || [block]).forEach(function (n) {
                if (dr[n] === undefined) return;
                if (COMBINED) state[n] = clone(dr[n]); else state = clone(dr[n]);
            });
            render();
        })
        .catch(function (e) { root.innerHTML = ''; root.appendChild(el('div', { class: 'cfg-error', text: String(e) })); });

    function changedClass(obj, key, orig) { return JSON.stringify(obj[key]) !== JSON.stringify(orig) ? ' cfg-changed' : ''; }
    function origAt(path) { var o = original; for (var i = 0; i < path.length; i++) { if (o == null) return undefined; o = o[path[i]]; } return o; }

    function toggle(obj, key, path) {
        var b = el('button', { type: 'button', class: 'cfg-toggle' + (obj[key] ? ' on' : '') });
        b.addEventListener('click', function () {
            obj[key] = !obj[key]; b.classList.toggle('on', !!obj[key]); update();
            if (key === 'Active' && path && ((path.length === 1 && !COMBINED && state === obj) || (path.length === 2 && COMBINED && state[path[0]] === obj) || (path.length === 2 && !COMBINED && block === 'Brain' && state[path[0]] === obj) || (path.length === 3 && COMBINED && block === 'Testing' && state[path[0]] && state[path[0]][path[1]] === obj))) render();
        });
        return b;
    }
    function num(obj, key, path, opts) {
        opts = opts || {};
        var i = el('input', { type: 'number', value: obj[key] == null || (opts.empty0 && !obj[key]) ? '' : obj[key], min: opts.min, max: opts.max, step: opts.step || 1, placeholder: opts.ph });
        i.addEventListener('input', function () {
            var v = i.value === '' ? 0 : Number(i.value);
            obj[key] = v; i.classList.toggle('cfg-changed', JSON.stringify(v) !== JSON.stringify(origAt(path))); update();
        });
        if (JSON.stringify(obj[key]) !== JSON.stringify(origAt(path))) i.classList.add('cfg-changed');
        return i;
    }
    function text(obj, key, path, validate) {
        var i = el('input', { type: 'text', value: obj[key] == null ? '' : obj[key] });
        i.addEventListener('input', function () {
            obj[key] = i.value; i.classList.toggle('cfg-changed', i.value !== origAt(path));
            i.style.borderColor = validate && !validate(i.value) ? 'var(--tb-bad)' : ''; update();
        });
        if (obj[key] !== origAt(path)) i.classList.add('cfg-changed');
        return i;
    }
    function select(obj, key, path, options) {
        var s = el('select');
        options.forEach(function (o) { var op = el('option', { value: o, text: o }); if (obj[key] === o) op.selected = true; s.appendChild(op); });
        s.addEventListener('change', function () { obj[key] = s.value; update(); });
        return s;
    }
    function row(label, hint, control, unit) {
        return el('div', { class: 'cfg-row' }, [
            el('div', { class: 'cfg-lab' }, [el('b', { text: label }), hint ? el('small', { text: hint }) : null]),
            el('div', null, [control, unit ? el('span', { class: 'cfg-unit', text: unit }) : null])
        ]);
    }
    function sec(title, kids, pill) {
        return el('section', { class: 'cfg-sec' }, [el('h3', null, [title, pill ? el('span', { class: 'cfg-pill' + (pill === 'new' ? ' new' : ''), text: pill }) : null])].concat(kids));
    }

    var handled = ['Active', 'Origin', 'ManualShips', 'PrimaryShip', 'MinPrimaryToSend', 'PrimaryToKeep', 'SleepWave'];

    var RETURN_EXAMPLES = [
        ['WakeUp', 'back exactly at wake-up'],
        ['WakeUp+15', '15 minutes after wake-up'],
        ['WakeUp-10', '10 minutes before wake-up'],
        ['07:30', 'fixed time of day'],
        ['', 'empty = fixed speed (see below)']
    ];
    function returnAtControl(sw) {
        var input = text(sw, 'ReturnAt', ['SleepWave', 'ReturnAt'], function (v) { return TIME_RE.test(v.trim()); });
        input.placeholder = 'e.g. WakeUp+15';
        var chips = el('div', { class: 'cfg-examples' }, [el('small', { text: 'Examples (click to use):' })]);
        RETURN_EXAMPLES.forEach(function (ex) {
            chips.appendChild(el('button', {
                type: 'button', class: 'cfg-chip', title: ex[1],
                text: ex[0] === '' ? '(empty)' : ex[0],
                onclick: function () { input.value = ex[0]; input.dispatchEvent(new Event('input', { bubbles: true })); }
            }));
        });
        var list = el('ul', { class: 'cfg-helplist' }, RETURN_EXAMPLES.map(function (ex) {
            return el('li', null, [el('code', { text: ex[0] === '' ? '(empty)' : ex[0] }), ' - ' + ex[1]]);
        }));
        return el('div', null, [input, chips, list]);
    }
    function renderExpeditions(body) {
        var d = state;
        if (d.Active === undefined) d.Active = false;
        body.appendChild(sec('General', [
            withHelp(row('Expeditions active', 'Master switch for the expedition worker', toggle(d, 'Active', ['Active'])), 'Expeditions.Active')
        ]));

        if (!Array.isArray(d.Origin)) d.Origin = [];
        var tbody = el('tbody');
        function originMax(o, p) {
            if (d.Origin.length <= 1) {
                var f = el('input', { type: 'text', disabled: 'disabled', value: '', placeholder: 'global: ' + (d.MaxExpeditionsPerOrigin || '-') });
                return f;
            }
            return num(o, 'MaxExpeditions', p.concat('MaxExpeditions'), { min: 0, empty0: true, ph: 'auto: slots / origins' });
        }
        function drawOrigins() {
            tbody.innerHTML = '';
            d.Origin.forEach(function (o, idx) {
                var p = ['Origin', idx];
                var tr = el('tr', null, [
                    el('td', null, [sens(num(o, 'Galaxy', p.concat('Galaxy'), { min: 1 }))]),
                    el('td', null, [sens(num(o, 'System', p.concat('System'), { min: 1 }))]),
                    el('td', null, [sens(num(o, 'Position', p.concat('Position'), { min: 1 }))]),
                    el('td', null, [select(o, 'Type', p.concat('Type'), ['Planet', 'Moon'])]),
                    el('td', null, [originMax(o, p)]),
                    el('td', null, [el('button', { type: 'button', class: 'cfg-btn sm', text: '✕', onclick: function () { d.Origin.splice(idx, 1); drawOrigins(); update(); } })])
                ]);
                tbody.appendChild(tr);
            });
            applyMask();
        }
        d.Origin.forEach(function (o) { if (!o.Type) o.Type = 'Planet'; });
        drawOrigins();
        body.appendChild(sec('Origins', [
            el('div', { class: 'cfg-note', text: 'One origin: the global limit (MaxExpeditionsPerOrigin) applies. Several origins: the expedition slots are distributed round-robin (13 slots: 7+6, with 3 origins 5+4+4); a value here overrides it for that origin (empty/0 = automatic).' }),
            el('table', { class: 'cfg-table' }, [
                el('thead', null, [el('tr', null, ['Galaxy', 'System', 'Position', 'Type', 'Max expeditions', ''].map(function (h) { return el('th', { text: h }); }))]),
                tbody
            ]),
            el('button', { type: 'button', class: 'cfg-btn sm', text: '+ Add origin', onclick: function () { d.Origin.push({ Galaxy: 1, System: 1, Position: 1, Type: 'Planet', MaxExpeditions: 0 }); drawOrigins(); update(); } })
        ], 'new'));

        if (!d.ManualShips) d.ManualShips = { Active: true, Ships: {} };
        if (!d.ManualShips.Ships) d.ManualShips.Ships = {};
        var ms = d.ManualShips;
        var fleetBox = el('div');
        var seg = el('div', { class: 'cfg-seg' });
        function drawSeg() {
            seg.innerHTML = '';
            [['Manual', true], ['Auto', false]].forEach(function (x) {
                var b = el('button', { type: 'button', text: x[0] + ' ships', class: ms.Active === x[1] ? 'on' : '' });
                b.addEventListener('click', function () { ms.Active = x[1]; drawSeg(); drawFleet(); update(); });
                seg.appendChild(b);
            });
        }
        function drawFleet() {
            fleetBox.innerHTML = '';
            if (ms.Active) {
                var grid = el('div', { class: 'cfg-ships' });
                var strongest = COMBAT.filter(function (s) { return s !== 'Pathfinder' && Number(ms.Ships[s]) > 0; })[0] || COMBAT.filter(function (s) { return Number(ms.Ships[s]) > 0; })[0];
                SHIPS.forEach(function (s) {
                    var v = Number(ms.Ships[s]) || 0;
                    var weaker = s !== 'Pathfinder' && COMBAT.indexOf(s) > -1 && v > 0 && strongest && COMBAT.indexOf(s) > COMBAT.indexOf(strongest);
                    var pfMany = s === 'Pathfinder' && v > 1;
                    var inp = el('input', { type: 'number', min: 0, value: v });
                    var card = el('div', { class: 'cfg-ship' + (weaker || pfMany ? ' red' : v > 0 ? ' on' : '') }, [el('span', { text: s }), inp]);
                    inp.addEventListener('input', function () { ms.Ships[s] = Number(inp.value) || 0; drawFleet(); update(); });
                    grid.appendChild(card);
                    if (pfMany) grid.appendChild(el('div', { class: 'cfg-ship-note', text: 'Pathfinder: 1 is enough - more than 1 adds nothing.' }));
                    if (weaker) grid.appendChild(el('div', { class: 'cfg-ship-note', text: s + ': superfluous - only the strongest combat ship (' + strongest + ') counts for finds.' }));
                });
                fleetBox.appendChild(grid);
                var checks = el('div', { class: 'cfg-checks' });
                var total = SHIPS.reduce(function (a, s) { return a + (Number(ms.Ships[s]) || 0); }, 0);
                if (!strongest) checks.appendChild(el('div', { class: 'wn', text: '⚠ No combat ship set - Pathfinder recommended.' }));
                else checks.appendChild(el('div', { class: 'ok', text: '✓ Strongest combat ship: ' + strongest }));
                if (!(Number(ms.Ships.EspionageProbe) > 0)) checks.appendChild(el('div', { class: 'wn', text: '⚠ No probe set (optional, sometimes useful).' }));
                if (total === 0) checks.appendChild(el('div', { class: 'bad', text: '✗ Fleet is empty.' }));
                fleetBox.appendChild(checks);
            } else {
                fleetBox.appendChild(withHelp(row('Primary ship', 'Cargo type that is calculated', select(d, 'PrimaryShip', ['PrimaryShip'], PRIMARY)), 'Expeditions.PrimaryShip'));
                if (!d.PrimaryShip) d.PrimaryShip = 'LargeCargo';
                fleetBox.appendChild(withHelp(row('Keep at home', 'PrimaryToKeep: ships of the primary type that stay home', num(d, 'PrimaryToKeep', ['PrimaryToKeep'], { min: 0 })), 'Expeditions.PrimaryToKeep'));
                fleetBox.appendChild(withHelp(row('Minimum to send', 'MinPrimaryToSend: no expedition if fewer primary ships are available (0 = off)', num(d, 'MinPrimaryToSend', ['MinPrimaryToSend'], { min: 0 })), 'Expeditions.MinPrimaryToSend'));
                fleetBox.appendChild(el('div', { class: 'cfg-note', text: 'Auto: cargo count from server top score and Lifeform bonus, plus 1 probe and the strongest combat ship (Pathfinder if none).' }));
            }
        }
        drawSeg(); drawFleet();
        body.appendChild(sec('Fleet (global)', [withHelp(row('Mode', null, seg), 'Expeditions.ManualShips.Active'), fleetBox]));

        if (!d.SleepWave) d.SleepWave = { Active: false, ReturnAt: '', SpeedPercent: 50 };
        var sw = d.SleepWave;
        body.appendChild(sec('Sleep wave', [
            withHelp(row('Active', 'Wave of expeditions that returns around wake-up', toggle(sw, 'Active', ['SleepWave', 'Active'])), 'Expeditions.SleepWave.Active'),
            withHelp(row('Return at', 'HH:mm, WakeUp or WakeUp+15 (minutes); empty = fixed speed', returnAtControl(sw)), 'Expeditions.SleepWave.ReturnAt'),
            withHelp(row('Speed (fallback)', 'Used when ReturnAt is empty', num(sw, 'SpeedPercent', ['SleepWave', 'SpeedPercent'], { min: 10, step: 10 }), '%'), 'Expeditions.SleepWave.SpeedPercent')
        ]));

        body.appendChild(lfPanel());
        body.appendChild(el('div', { class: 'cfg-placeholder-card' }, [
            el('h3', { text: 'Test field / dry run (placeholder)' }),
            el('div', { text: 'Will show the fleet the bot would send right now, without sending it.' })
        ]));

        var rest = Object.keys(d).filter(function (k) { return handled.indexOf(k) < 0; });
        if (rest.length) {
            var det = el('details', { class: 'cfg-sec cfg-more' }, [
                el('summary', { text: 'More settings (' + rest.length + ')' }),
                genericObject(d, rest, [])
            ]);
            if (moreOpen) det.open = true;
            det.addEventListener('toggle', function () { moreOpen = det.open; });
            body.appendChild(det);
        }
    }

    function allyNamesBox(W) {
        var box = el('div', { class: 'cfg-note', style: 'display:none' });
        sens(box);
        var names = null;
        function draw() {
            var ids = W.AdditionalAllianceIds || [];
            var parts = [];
            if (names) {
                var own = names.filter(function (x) { return x.own; })[0];
                if (own) parts.push('Own: ' + (own.tag ? '[' + own.tag + '] ' : '') + own.name);
                ids.forEach(function (id) {
                    var m = names.filter(function (x) { return x.id === id; })[0];
                    parts.push(id + ' = ' + (m ? (m.tag ? '[' + m.tag + '] ' : '') + m.name : 'name not loaded yet'));
                });
            }
            box.dataset.v = parts.join('   |   ');
            if (!box.dataset.m) box.textContent = box.dataset.v;
            box.style.display = parts.length ? '' : 'none';
        }
        fetch(main.dataset.allynames + '?instance=' + encodeURIComponent(instance), { cache: 'no-store' })
            .then(function (r) { return r.json(); })
            .then(function (r) { if (r.success) { names = r.names; draw(); } })
            .catch(function () { });
        box.redraw = draw;
        allyBox = box;
        return box;
    }
    var allyBox = null;

    var lossOpen = true;
    function lossPanel() {
        var box = el('details', { class: 'cfg-sec cfg-more' }, [el('summary', { text: 'Losses and profit' })]);
        box.open = lossOpen;
        box.addEventListener('toggle', function () { lossOpen = box.open; });
        var content = el('div', { class: 'cfg-note', text: 'Loading…' });
        box.appendChild(content);
        function n(v) { return Math.round(Number(v) || 0).toLocaleString(); }
        fetch(main.dataset.aflosses + '?instance=' + encodeURIComponent(instance), { cache: 'no-store' })
            .then(function (r) { return r.json(); })
            .then(function (r) {
                content.innerHTML = '';
                if (!r.success) { content.textContent = r.error; return; }
                if (!r.periods.length) { content.textContent = 'No data yet - AutoFarm has to send and receive attacks first.'; return; }
                function three(set) { var q = set || {}; return 'Met ' + n(q.metal) + ' | Kris ' + n(q.crystal) + ' | Deut ' + n(q.deuterium); }
                r.periods.forEach(function (p) {
                    var ships = (p.ships || []).length ? p.ships.map(function (s) { return n(s.count) + '× ' + s.ship; }).join(', ') : 'none';
                    var hasLoot = p.actualCount > 0;
                    var sub = p.attacks + ' attacks' + (p.destroyed ? ', ' + p.destroyed + ' destroyed' : '') + (p.unknown ? ', ' + p.unknown + ' unknown' : '') + (p.pending ? ', ' + p.pending + ' open' : '');
                    var net = p.netRes || {};
                    var good = (net.metal || 0) >= 0 && (net.crystal || 0) >= 0 && (net.deuterium || 0) >= 0;
                    content.appendChild(row(p.label, sub, el('span', {
                        text: hasLoot ? 'Net ' + three(net) : 'no loot recorded yet',
                        style: hasLoot ? (good ? 'color:var(--tb-ok)' : 'color:var(--tb-bad)') : ''
                    })));
                    content.appendChild(el('div', { class: 'cfg-note', text: 'Loot ' + (hasLoot ? three(p.actualRes) : '-') + ' (planned ' + three(p.plannedRes) + ')' }));
                    content.appendChild(el('div', { class: 'cfg-note', text: 'Ship losses ' + three(p.lossRes) + ' | fuel Deut ' + n(p.fuel) + ' | lost: ' + ships }));
                });
                content.appendChild(el('div', { class: 'cfg-note', text: 'Net = actual loot (cargo of the returning fleet) − ship losses − fuel (deuterium), per resource. Unobserved fleets are not counted.' }));
            })
            .catch(function (er) { content.textContent = String(er); });
        return box;
    }

    function lfPanel() {
        var box = el('details', { class: 'cfg-sec cfg-more' }, [el('summary', { text: 'Lifeform effects on expeditions' })]);
        if (lfOpen) box.open = true;
        box.addEventListener('toggle', function () { lfOpen = box.open; });
        var content = el('div', { class: 'cfg-note', text: 'Loading…' });
        box.appendChild(content);
        fetch(main.dataset.lf + '?instance=' + encodeURIComponent(instance), { cache: 'no-store' })
            .then(function (r) { return r.json(); })
            .then(function (r) {
                content.innerHTML = '';
                if (!r.success) { content.textContent = r.error; return; }
                var d = r.data, e = d.expeditions || {}, c = d.cargo || {};
                function pct(v) { return (Math.round(v * 1000) / 10) + ' %'; }
                var rows = [['Resources found', e.Resources], ['Large cargo capacity', c.LargeCargo]];
                rows.forEach(function (x) {
                    var v = Number(x[1]) || 0;
                    content.appendChild(row(x[0], null, el('span', { text: v ? pct(v) : '0 % (not researched)', style: v ? 'color:var(--tb-ok)' : 'color:var(--tb-warn)' })));
                });
                var tiers = [[10000, 40000], [100000, 500000], [1000000, 1200000], [5000000, 1800000], [25000000, 2400000], [50000000, 3000000], [75000000, 3600000], [100000000, 4200000]];
                var cap = 5000000;
                for (var t = 0; t < tiers.length; t++) { if (d.topScore < tiers[t][0]) { cap = tiers[t][1]; break; } }
                var disc = d.playerClass === 'Discoverer';
                cap = disc ? cap * (d.ecoSpeed || 1) * 3 : cap * 2;
                cap = Math.ceil(cap * (1 + (Number(e.Resources) || 0)));
                content.appendChild(row('Top 1 highscore', 'Decides the maximum find per expedition', el('span', { text: Math.round(d.topScore).toLocaleString() + ' points' })));
                content.appendChild(row('Max find per expedition', (disc ? 'Discoverer' : 'other class') + ', economy speed ' + (d.ecoSpeed || 1) + ' (without tech 18)', el('span', { text: cap.toLocaleString() + ' resources' })));
                content.appendChild(row('Discoverer class bonus (tech 18)', 'Kaelesh: raises the class bonus', el('span', { text: d.discovererClass ? pct(d.discovererClass) : '0 % (not researched)', style: d.discovererClass ? 'color:var(--tb-ok)' : 'color:var(--tb-warn)' })));
                content.appendChild(el('div', { class: 'cfg-note', text: 'Only what matters for the cargo count: finds per expedition (resources, class bonus) and cargo capacity. The auto fleet calculation uses Resources and cargo; the tech-18 class bonus is not used yet.' }));
                content.appendChild(el('div', { class: 'cfg-note', text: 'As of ' + new Date(d.updatedUtc).toLocaleString() + '. Values are taken from the game by the Expeditions worker.' }));
            })
            .catch(function (er) { content.textContent = String(er); });
        return box;
    }

    function foldTo(body, title, kids) {
        var det = el('details', { class: 'cfg-sec cfg-more' }, [el('summary', { text: title })].concat(kids.filter(Boolean)));
        if (openSecs[title]) det.open = true;
        det.addEventListener('toggle', function () { openSecs[title] = det.open; });
        body.appendChild(det);
    }
    function lines(o, k, p, ph) {
        var t = el('textarea', { rows: 4, placeholder: ph || 'one entry per line', style: 'width:100%' });
        t.value = (o[k] || []).join('\n');
        t.addEventListener('input', function () {
            o[k] = t.value.split('\n').map(function (x) { return x.trim(); }).filter(Boolean);
            t.classList.toggle('cfg-changed', JSON.stringify(o[k]) !== JSON.stringify(origAt(p))); update();
        });
        return t;
    }
    var TIME_HM = /^\d{1,2}:\d{2}$/;

    function renderDefender(body) {
        var d = state, P = [];
        function sub(name) { return d[name] && typeof d[name] === 'object' && !Array.isArray(d[name]) ? d[name] : null; }
        body.appendChild(sec('General', [
            fld(d, 'Active', P, 'Defender active', 'Master switch', 'toggle'),
            fld(d, 'IgnoreProbes', P, 'Ignore spy probes', null, 'toggle'),
            fld(d, 'DefendFromMissiles', P, 'React to missile attacks', null, 'toggle'),
            fld(d, 'RandomActivity', P, 'Generate activity for defender calls', 'On: each defender check opens the defences page of a random celestial. Off: it always opens the one from "Home celestial". Either way it only creates normal-looking activity (the check itself is already a page visit) and does not change how attacks are handled.', 'toggle')
        ].filter(Boolean)));

        var weak = [fld(d, 'IgnoreWeakAttack', P, 'Ignore weak attackers', null, 'toggle'),
            fld(d, 'WeakAttackRatio', P, 'Weak attack ratio', 'Attacker points below your fleet points / ratio', 'num', { min: 0, step: 0.5 })];
        var ig = sub('IgnoreAttackIfIHave');
        if (ig) {
            var ip = ['IgnoreAttackIfIHave'];
            weak.push(el('div', { class: 'cfg-gname', text: 'Nothing worth saving' }));
            weak.push(fld(ig, 'Active', ip, 'Ignore if nothing worth saving', null, 'toggle'));
            weak.push(fld(ig, 'MinResourcesToSave', ip, 'Min resources to save', null, 'num', { min: 0 }));
            weak.push(fld(ig, 'MinFleetToSave', ip, 'Min fleet points to save', null, 'num', { min: 0 }));
        }
        foldTo(body, 'Weak attacks', weak);

        var home = sub('Home'), hp = ['Home'];
        if (home) foldTo(body, 'Home celestial', [
            el('div', { class: 'cfg-note', text: 'The celestial the defender looks at for its activity calls when "Generate activity" is off. AutoHarvest also uses it as the default origin (the moon at these coordinates) if it has no Origin of its own. Set it to your main planet or moon.' }),
            fld(home, 'Galaxy', hp, 'Galaxy', null, 'num', { min: 1, sens: true }),
            fld(home, 'System', hp, 'System', null, 'num', { min: 1, sens: true }),
            fld(home, 'Position', hp, 'Position', null, 'num', { min: 1, sens: true }),
            fld(home, 'Type', hp, 'Type', null, 'select', { options: ['Planet', 'Moon'] })
        ]);

        var af = sub('Autofleet'), ap = ['Autofleet'];
        if (af) {
            var afk = [fld(af, 'Active', ap, 'Fleetsave the attacked celestial', null, 'toggle'),
                fld(af, 'SafetyMarginSeconds', ap, 'Safety margin', 'Fleet must be gone this long before impact', 'num', { min: 0, unit: 'seconds' })];
            if (af.TelegramMessenger && typeof af.TelegramMessenger === 'object')
                afk.push(fld(af.TelegramMessenger, 'Active', ap.concat('TelegramMessenger'), 'Telegram message on fleetsave', null, 'toggle'));
            foldTo(body, 'Fleetsave (Autofleet)', afk);
        }

        var al = [];
        var dc = sub('Discord');
        if (dc) {
            al.push(fld(dc, 'Active', ['Discord'], 'Discord alert', 'Webhook URL is not shown here (credentials)', 'toggle'));
            al.push(withHelp(row('Discord webhook', 'Set it on the Messenger page', el('span', { class: 'cfg-note', text: dc.WebhookUrl ? '✓ set' : 'not set' })), 'Defender.Discord.WebhookUrl'));
        }
        var tg = sub('TelegramMessenger');
        if (tg) al.push(fld(tg, 'Active', ['TelegramMessenger'], 'Telegram alert', null, 'toggle'));
        var alarm = sub('Alarm');
        if (alarm) al.push(fld(alarm, 'Active', ['Alarm'], 'Alarm level in the alert', null, 'toggle'));
        if (alarm) {
            if (!has(alarm, 'SoundFile')) alarm.SoundFile = '';
            al.push(fld(alarm, 'SoundFile', ['Alarm'], 'Alarm sound (WAV file)', 'Full path to a WAV file played on an attack; empty = beep', 'text'));
        }
        foldTo(body, 'Alerts', al);

        var re = [];
        var sp = sub('SpyAttacker');
        if (sp) {
            re.push(fld(sp, 'Active', ['SpyAttacker'], 'Spy the attacker', null, 'toggle'));
            re.push(fld(sp, 'Probes', ['SpyAttacker'], 'Probes', null, 'num', { min: 1 }));
        }
        var ma = sub('MessageAttacker');
        if (ma) {
            re.push(fld(ma, 'Active', ['MessageAttacker'], 'Message the attacker', 'One of the messages is sent', 'toggle'));
            if (Array.isArray(ma.Messages)) re.push(withHelp(row('Messages', 'One per line', lines(ma, 'Messages', ['MessageAttacker', 'Messages'])), 'Defender.MessageAttacker.Messages'));
        }
        foldTo(body, 'Reaction to the attacker', re);

        if (Array.isArray(d.WhiteList)) foldTo(body, 'Whitelist', [
            el('div', { class: 'cfg-note', text: 'Player IDs that are never treated as attackers (0 = placeholder).' }),
            fld(d, 'WhiteList', P, 'Player IDs', 'Comma separated', 'intlist', { sens: true })
        ]);

        foldTo(body, 'Timing', [
            fld(d, 'CheckIntervalMin', P, 'Check interval, min', null, 'num', { min: 0, unit: 'seconds' }),
            fld(d, 'CheckIntervalMax', P, 'Check interval, max', null, 'num', { min: 0, unit: 'seconds' })
        ]);

        var known = ['Active', 'IgnoreProbes', 'DefendFromMissiles', 'RandomActivity', 'IgnoreWeakAttack', 'WeakAttackRatio', 'IgnoreAttackIfIHave', 'Home', 'Autofleet', 'Discord', 'TelegramMessenger', 'Alarm', 'SpyAttacker', 'MessageAttacker', 'WhiteList', 'CheckIntervalMin', 'CheckIntervalMax'];
        var rest = Object.keys(d).filter(function (k) { return known.indexOf(k) < 0; });
        if (rest.length) foldTo(body, 'More settings (' + rest.length + ')', [genericObject(d, rest, [])]);
    }

    function renderGhostSleep(body, d, P) {
        var timeOk = function (v) { return TIME_HM.test(v.trim()); };
        if (!has(d, 'AnchorMode')) d.AnchorMode = 'LastFleet';
        if (!d.ExpeditionDrain || typeof d.ExpeditionDrain !== 'object') d.ExpeditionDrain = { Active: false, BufferMinutes: 10, StragglerWaitMinutes: 20, DefaultDurationMinutes: 90 };
        body.appendChild(sec('GhostSleep', [
            el('div', { class: 'cfg-warnbox' }, [
                el('b', { text: 'This is not a real sleep. ' }),
                document.createTextNode('GhostSleep only reduces activity: the account keeps making requests (expeditions keep flying, the Defender checks around expedition returns, everything restarts at wake-up). It looks quieter than a normal bot, but not like a player who is really offline. Do not leave it on every day - use it now and then, and keep real offline time or the normal SleepMode for regular nights.')
            ]),
            el('div', { class: 'cfg-note', text: 'GhostSleep is a second, independent sleep window (the timed version of the Telegram command /ghost). During it the bot stops everything except Expeditions, Defender and SleepMode and fleetsaves all ships with a ghost flight, so your fleet is never at home. Expeditions keep flying; the Defender only alerts (no automatic fleetsave) and polls rarely to look inactive, and switches to full defence if an attack wave is detected. Use it when you want to look inactive but keep expeditions running.' }),
            fld(d, 'GoToSleep', P, 'Go to sleep', 'Server time, HH:mm', 'text', { validate: timeOk }),
            fld(d, 'WakeUp', P, 'Wake up', 'Server time, HH:mm', 'text', { validate: timeOk }),
            fld(d, 'PreSleepExpeditionMinutes', P, 'Refill expeditions before sleep', '0 = off', 'num', { min: 0, unit: 'minutes before' }),
            fld(d, 'AnchorMode', P, 'Defender check while asleep', 'LastFleet = shortly before the last expedition lands; Random = a random returning expedition', 'select', { options: ['LastFleet', 'Random'] })
        ].filter(Boolean)));
        if (d.ExpeditionDrain && typeof d.ExpeditionDrain === 'object') {
            var DP = P.concat('ExpeditionDrain');
            body.appendChild(sec('GhostSleep: expeditions before sleep', [
                el('div', { class: 'cfg-note', text: 'Replaces the time-based refill above. Shortly before sleeping no expedition is started that would not be back in time (duration is measured). At bed time the bot waits for stragglers, sends one simultaneous wave and goes to sleep.' }),
                fld(d.ExpeditionDrain, 'Active', DP, 'Let expeditions finish before sleep', null, 'toggle'),
                fld(d.ExpeditionDrain, 'BufferMinutes', DP, 'Tolerance', 'An expedition may be back this long after go-to-sleep', 'num', { min: 0, unit: 'minutes' }),
                fld(d.ExpeditionDrain, 'StragglerWaitMinutes', DP, 'Wait for stragglers', 'At bed time, at most', 'num', { min: 0, unit: 'minutes' }),
                fld(d.ExpeditionDrain, 'DefaultDurationMinutes', DP, 'Duration until measured', 'Used until 3 real durations are known', 'num', { min: 1, unit: 'minutes' })
            ].filter(Boolean)));
        }
        if (Array.isArray(d.PriorityFleetsaveCelestials)) foldTo(body, 'GhostSleep: priority fleetsave celestials', [
            el('div', { class: 'cfg-note', text: 'Moon and planet coordinates that are fleetsaved first.' }),
            listTable(d.PriorityFleetsaveCelestials,
                [{ key: 'Galaxy', label: 'Galaxy', min: 1, sens: true }, { key: 'System', label: 'System', min: 1, sens: true }, { key: 'Position', label: 'Position', min: 1, sens: true }],
                function () { return { Galaxy: 1, System: 1, Position: 1 }; }, '+ Add celestial', ['PriorityFleetsaveCelestials'])
        ]);
        var rest = Object.keys(d).filter(function (k) { return ['Active', 'GoToSleep', 'WakeUp', 'PreSleepExpeditionMinutes', 'AnchorMode', 'ExpeditionDrain', 'PriorityFleetsaveCelestials'].indexOf(k) < 0; });
        if (rest.length) foldTo(body, 'GhostSleep: more settings (' + rest.length + ')', [genericObject(d, rest, P)]);
    }

    var SLEEP_TEXT = {
        off: 'No sleep window: the bot runs around the clock.',
        sleep: 'SleepMode: in the window the bot sends no fleets at all and can fleetsave once when going to sleep.',
        ghost: 'GhostSleep: in the window all ships are fleetsaved with a ghost flight; expeditions keep flying and the Defender stays on watch. Warning: this is NOT a real sleep, only reduced activity - do not leave it on every day.',
        both: 'Both are switched on in the config - they should not run at the same time. Pick one.'
    };
    function sleepCur(sm, gs) { return sm.Active && gs && gs.Active ? 'both' : (gs && gs.Active ? 'ghost' : (sm.Active ? 'sleep' : 'off')); }
    function sleepModeSwitch(sm, gs) {
        var cur = function () { return sleepCur(sm, gs); };
        var opts = [['off', 'Off'], ['sleep', 'Sleep']].concat(gs ? [['ghost', 'GhostSleep']] : []);
        var wrap = el('div', { class: 'cfg-seg' }), note = el('div', { class: 'cfg-note', text: SLEEP_TEXT[cur()] }), btns = {};
        function mark() {
            var c = cur();
            opts.forEach(function (o) { btns[o[0]].classList.toggle('on', o[0] === c); });
            note.textContent = SLEEP_TEXT[c];
            wrap.classList.toggle('cfg-changed', JSON.stringify([sm.Active, gs && gs.Active]) !== JSON.stringify([original.SleepMode.Active, original.GhostSleep && original.GhostSleep.Active]));
        }
        opts.forEach(function (o) {
            var b = el('button', { type: 'button', text: o[1] });
            b.addEventListener('click', function () { sm.Active = o[0] === 'sleep'; if (gs) gs.Active = o[0] === 'ghost'; update(); render(); });
            btns[o[0]] = b; wrap.appendChild(b);
        });
        mark();
        return sec('Mode', [withHelp(row('Sleep mode', 'Only one of them can be active', wrap), 'SleepMode.Mode'), note]);
    }
    function renderSleepMode(body, d, P) {
        var timeOk = function (v) { return TIME_HM.test(v.trim()); };
        body.appendChild(sec('SleepMode', [
            el('div', { class: 'cfg-note', text: 'SleepMode is the classic sleep: in the window below the bot sends no fleets at all (most workers pause) and can fleetsave your ships once when going to sleep. Compare GhostSleep further down, which keeps expeditions and defence running.' }),
            fld(d, 'GoToSleep', P, 'Go to sleep', 'Server time, HH:mm', 'text', { validate: timeOk }),
            fld(d, 'WakeUp', P, 'Wake up', 'Server time, HH:mm', 'text', { validate: timeOk }),
            fld(d, 'PreventIfThereAreFleets', P, 'Delay sleep while fleets are out', 'Wait while fleets would come back during the sleep window', 'toggle'),
            d.TelegramMessenger && typeof d.TelegramMessenger === 'object' ? fld(d.TelegramMessenger, 'Active', P.concat('TelegramMessenger'), 'Telegram message', 'On going to sleep and waking up', 'toggle') : null
        ].filter(Boolean)));
        var fs = d.AutoFleetSave && typeof d.AutoFleetSave === 'object' ? d.AutoFleetSave : null, fp = P.concat('AutoFleetSave');
        if (fs) {
            var kids = [
                fld(fs, 'Active', fp, 'Fleetsave when going to sleep', null, 'toggle'),
                fld(fs, 'OnlyMoons', fp, 'Only moons', null, 'toggle'),
                fld(fs, 'DeutToLeave', fp, 'Deuterium to leave', 'The rest goes with the fleet as fuel', 'num', { min: 0 }),
                fld(fs, 'Recall', fp, 'Recall at wake-up', 'Recall the saving fleet so it is back when you wake up', 'toggle'),
                fld(fs, 'DefaultMission', fp, 'Mission', 'Deploy = station the ships at another of your moons (recommended)', 'select', { options: ['Deploy', 'Harvest', 'Transport', 'Spy', 'Colonize'].concat(['Deploy', 'Harvest', 'Transport', 'Spy', 'Colonize'].indexOf(fs.DefaultMission) < 0 && fs.DefaultMission ? [fs.DefaultMission] : []) })
            ];
            var dt = fs.DeployTarget && typeof fs.DeployTarget === 'object' ? fs.DeployTarget : null, dp = fp.concat('DeployTarget');
            if (dt) {
                kids.push(el('div', { class: 'cfg-gname', text: 'Deploy target' }));
                kids.push(el('div', { class: 'cfg-note', text: 'Optional fixed target moon. If these coordinates are not one of your moons, or the flight does not fit, the bot picks a suitable moon of yours on its own - you do not have to choose one.' }));
                kids.push(fld(dt, 'Galaxy', dp, 'Galaxy', null, 'num', { min: 1, sens: true }));
                kids.push(fld(dt, 'System', dp, 'System', null, 'num', { min: 1, sens: true }));
                kids.push(fld(dt, 'Position', dp, 'Position', null, 'num', { min: 1, sens: true }));
            }
            foldTo(body, 'SleepMode: fleetsave', kids.filter(Boolean));
        }
        var known = ['Active', 'GoToSleep', 'WakeUp', 'PreventIfThereAreFleets', 'TelegramMessenger', 'AutoFleetSave'];
        var rest = Object.keys(d).filter(function (k) { return known.indexOf(k) < 0; });
        if (rest.length) foldTo(body, 'SleepMode: more settings (' + rest.length + ')', [genericObject(d, rest, P)]);
    }
    function renderSleepPage(body) {
        body.appendChild(sleepModeSwitch(state.SleepMode, state.GhostSleep));
        var cur = sleepCur(state.SleepMode, state.GhostSleep);
        if (cur === 'sleep') renderSleepMode(body, state.SleepMode, ['SleepMode']);
        else if (cur === 'ghost') renderGhostSleep(body, state.GhostSleep, ['GhostSleep']);
        else body.appendChild(sec('What they do', [
            el('div', { class: 'cfg-note', text: 'Pick Sleep or GhostSleep above to show its options. Your settings of both are kept.' }),
            el('ul', { class: 'cfg-helplist' }, [
                el('li', null, [el('b', { text: 'Sleep: ' }), 'Classic sleep window. The bot sends no fleets at all (most workers pause) and can fleetsave your ships once when going to sleep; the fleet can be recalled at wake-up.']),
                el('li', null, [el('b', { text: 'GhostSleep: ' }), 'Second, independent window (timed version of /ghost). Everything stops except Expeditions, Defender and SleepMode, and all ships are fleetsaved with a ghost flight. Expeditions keep flying, the Defender only alerts (no automatic fleetsave) and polls rarely, and escalates to full defence if an attack wave is detected.'])
            ])
        ]));
    }


    function addDefault(parent, key, dflt, label, hint) {
        var b = el('button', { type: 'button', class: 'cfg-btn', text: 'Add with default ' + (typeof dflt === 'object' ? '' : dflt) });
        b.addEventListener('click', function () { parent[key] = JSON.parse(JSON.stringify(dflt)); update(); render(); });
        return row(label, hint, b);
    }
    function testingField(o, key, dflt, path, label, hint, kind, opts) {
        return has(o, key) ? fld(o, key, path, label, hint, kind, opts) : addDefault(o, key, dflt, label, hint);
    }
    function renderTestingPage(body) {
        var X = state.Experimental, G = state.General, B = state.Brain;
        body.appendChild(el('div', { class: 'cfg-note', text: 'New functions that are still in testing. They are kept apart from the normal pages (General, Brain) until they have proven themselves. Switch one on, watch the log and report problems. When a function has proven itself it moves back to its normal page.' }));

        if (X && typeof X === 'object') {
            var xp = ['Experimental'];
            if (X.BotPause && typeof X.BotPause === 'object') {
                var bp = X.BotPause, bpp = xp.concat('BotPause');
                body.appendChild(sec('Bot pause (Pause button and /pause)', [
                    el('div', { class: 'cfg-note', text: 'The Pause button in the browser tile (or /pause in Telegram) stops all functions except the Defender for a few minutes, so nothing new starts while you play by hand. After the time the bot goes on by itself. It is refused if a recall is due in that time. This block is saved as "Experimental" in the config.' }),
                    fld(bp, 'Active', bpp, 'Pause function active', 'Off: the Pause button and /pause are refused', 'toggle'),
                    fld(bp, 'DurationMinutes', bpp, 'Pause length', 'Used when you press the button or send /pause without a time', 'num', { min: 1, unit: 'minutes' }),
                    fld(bp, 'RecallMarginMinutes', bpp, 'Margin before a recall', 'A recall due within the pause time plus this margin blocks the pause', 'num', { min: 0, unit: 'minutes' }),
                    fld(bp, 'MaxDurationMinutes', bpp, 'Longest pause', 'Upper limit for /pause with a time', 'num', { min: 1, unit: 'minutes' })
                ].filter(Boolean)));
            } else {
                body.appendChild(sec('Bot pause (Pause button and /pause)', [
                    el('div', { class: 'cfg-note', text: 'Not in your config yet - the defaults (15 minutes, margin 2, longest 120) are used. Add it to change them.' }),
                    addDefault(X, 'BotPause', { Active: true, DurationMinutes: 15, RecallMarginMinutes: 2, MaxDurationMinutes: 120 }, 'Bot pause settings', null)
                ]));
            }
            if (X.DefenderRecheck && typeof X.DefenderRecheck === 'object') {
                var dr = X.DefenderRecheck, drp = xp.concat('DefenderRecheck');
                body.appendChild(sec('Defender: check again before the fleetsave', [
                    el('div', { class: 'cfg-note', text: 'Shortly before a fleetsave the Defender asks the game for the attack list once more. Attack recalled: no fleetsave. Attack later: the fleetsave is moved. If the question fails or takes too long, the fleetsave goes out as planned.' }),
                    fld(dr, 'Active', drp, 'Check again before the fleetsave', 'Off by default', 'toggle'),
                    fld(dr, 'TimeoutSeconds', drp, 'Wait for the answer', 'After this the fleetsave is sent as planned', 'num', { min: 1, unit: 'seconds' })
                ].filter(Boolean)));
            } else {
                body.appendChild(sec('Defender: check again before the fleetsave', [
                    el('div', { class: 'cfg-note', text: 'Not in your config yet (off). Add it to switch it on.' }),
                    addDefault(X, 'DefenderRecheck', { Active: false, TimeoutSeconds: 4 }, 'Defender recheck settings', null)
                ]));
            }
            if (X.FleetsaveSlot && typeof X.FleetsaveSlot === 'object') {
                var fs = X.FleetsaveSlot, fsp = xp.concat('FleetsaveSlot');
                body.appendChild(sec('Reserved slot for the fleetsave', [
                    el('div', { class: 'cfg-note', text: 'The last free fleet slot is kept for the fleetsave. All other fleets, even forced ones, are refused when only the reserved slot is left. Refusals appear in the log as SLOTCOLLISION.' }),
                    fld(fs, 'Active', fsp, 'Reserve a slot for the fleetsave', 'On by default', 'toggle'),
                    fld(fs, 'Slots', fsp, 'Reserved slots', 'Works on top of "Slots to leave free"', 'num', { min: 0 })
                ].filter(Boolean)));
            } else {
                body.appendChild(sec('Reserved slot for the fleetsave', [
                    el('div', { class: 'cfg-note', text: 'Not in your config yet - the default (1 slot reserved) applies. Add it to change that.' }),
                    addDefault(X, 'FleetsaveSlot', { Active: true, Slots: 1 }, 'Fleetsave slot settings', null)
                ]));
            }
            if (X.MinePlanRanking && typeof X.MinePlanRanking === 'object') {
                var mr = X.MinePlanRanking, mrp = xp.concat('MinePlanRanking');
                body.appendChild(sec('Mine plan: ranking of the next steps', [
                    el('div', { class: 'cfg-note', text: 'How the page Minenplan sorts the best next investments. Mines and research (plasma technology) are ranked together. Display only: AutoMine and AutoResearch build by their own rules.' }),
                    fld(mr, 'Mode', mrp, 'Ranking', 'Payback = like AutoMine; Cheapest = lowest cost first; Npv = most gain over the horizon', 'select', { options: ['Payback', 'Cheapest', 'Npv'] }),
                    fld(mr, 'HorizonDays', mrp, 'Horizon (Npv)', 'Steps that do not pay back within this time are left out', 'num', { min: 1, unit: 'days' }),
                    fld(mr, 'OnlySensible', mrp, 'Only sensible steps', 'Hide steps with a payback longer than the limit', 'toggle'),
                    fld(mr, 'MaxPaybackDays', mrp, 'Longest sensible payback', null, 'num', { min: 1, unit: 'days' }),
                    fld(mr, 'IncludeResearch', mrp, 'Include research', 'Rank plasma technology next to the mine steps', 'toggle')
                ].filter(Boolean)));
            } else {
                body.appendChild(sec('Mine plan: ranking of the next steps', [
                    el('div', { class: 'cfg-note', text: 'Not in your config yet - the default (shortest payback, research included) applies. Add it to change that.' }),
                    addDefault(X, 'MinePlanRanking', { Mode: 'Payback', HorizonDays: 90, MaxPaybackDays: 200, OnlySensible: false, IncludeResearch: true }, 'Mine plan ranking settings', null)
                ]));
            }
            if (X.AsteroidHunt && typeof X.AsteroidHunt === 'object') {
                var ah = X.AsteroidHunt, ahp = xp.concat('AsteroidHunt');
                body.appendChild(sec('Asteroid item hunter (item event)', [
                    el('div', { class: 'cfg-note', text: 'Once per game day one ship attacks the asteroid on position 17 of your own system. After arrival the bot checks whether the inventory grew. One short log line per attempt, no messenger message.' }),
                    fld(ah, 'Active', ahp, 'Hunt asteroids', 'Off by default. Only useful while the item event runs', 'toggle'),
                    fld(ah, 'ShipType', ahp, 'Ship', 'Type the ship name exactly as in the game (English), e.g. Pathfinder, Cruiser, Reaper, Destroyer. No espionage probes. The planet with the most of this ship sends it.', 'text', {}),
                    fld(ah, 'ShipCount', ahp, 'Number of ships', null, 'num', { min: 1 }),
                    fld(ah, 'MaxAttemptsPerDay', ahp, 'Attempts per game day', '0 = unlimited (stops after the first find)', 'num', { min: 0 }),
                    fld(ah, 'SearchSystems', ahp, 'Systems to search', 'The asteroid is rarely in the home system. Tried: own, +1, -1, +2 ...', 'num', { min: 1 }),
                    fld(ah, 'SaveJson', ahp, 'Keep a JSON log', 'data/asteroiden_funde_<alias>.json', 'toggle')
                ].filter(Boolean)));
            } else {
                body.appendChild(sec('Asteroid item hunter (item event)', [
                    el('div', { class: 'cfg-note', text: 'Not in your config yet (off). Add it to switch it on.' }),
                    addDefault(X, 'AsteroidHunt', { Active: false, ShipType: 'Pathfinder', ShipCount: 1, MaxAttemptsPerDay: 0, SearchSystems: 10, SaveJson: true }, 'Asteroid hunter settings', null)
                ]));
            }
            if (X.ShadowFleet && typeof X.ShadowFleet === 'object') {
                var sf = X.ShadowFleet, sfp = xp.concat('ShadowFleet');
                var cel = sf.Celestial && typeof sf.Celestial === 'object' ? sf.Celestial : (sf.Celestial = { Galaxy: 1, System: 1, Position: 1, Type: 'Planet' });
                var celRows = [
                    fld(cel, 'Galaxy', sfp.concat('Celestial'), 'Galaxy', null, 'num', { min: 1, sens: true }),
                    fld(cel, 'System', sfp.concat('Celestial'), 'System', null, 'num', { min: 1, sens: true }),
                    fld(cel, 'Position', sfp.concat('Celestial'), 'Position', null, 'num', { min: 1, sens: true }),
                    fld(cel, 'Type', sfp.concat('Celestial'), 'Type', 'Planet or moon', 'select', { options: ['Planet', 'Moon'] })
                ];
                body.appendChild(sec('Shadow fleet', [
                    el('div', { class: 'cfg-warnbox' }, [el('b', { text: 'Deuterium reserve needed. ' }), document.createTextNode('Every flight burns fuel and the fleet carries deuterium for the chosen number of flights. Keep enough deuterium on the celestial; when it is used up the flights stop (a warning comes when only one flight is left).')]),
                    el('div', { class: 'cfg-note', text: 'The chosen ships of ONE celestial fly one after another to nearby debris fields and are recalled after half of the time, so the fleet is almost never at home. Only that celestial\'s deuterium and one fleet slot are used.' }),
                    fld(sf, 'Active', sfp, 'Shadow fleet', 'Off by default', 'toggle')
                ].filter(Boolean)));
                body.appendChild(sec('Shadow fleet: celestial and ships', celRows.concat([
                    withHelp(row('Ships', 'Tick the ships and enter the amount. At least one Recycler (or Pathfinder) is needed.', shipPicker(sf, 'Ships', sfp.concat('Ships'))), 'ShadowFleet.Ships'),
                    fld(sf, 'SpeedPercent', sfp, 'Speed', '10 = slowest and cheapest', 'num', { min: 10, max: 100, step: 10, unit: '%' }),
                    fld(sf, 'DeuteriumFlights', sfp, 'Deuterium as cargo for', 'Number of flights; limited by cargo space and stock', 'num', { min: 0, unit: 'flights' })
                ]).filter(Boolean)));
                body.appendChild(sec('Shadow fleet: time', [
                    fld(sf, 'MinBackInMinutes', sfp, 'One flight lasts at least', 'Out and back', 'num', { min: 2, unit: 'minutes' }),
                    fld(sf, 'MaxBackInMinutes', sfp, 'One flight lasts at most', 'Time limit: the fleet is home by then', 'num', { min: 2, unit: 'minutes' }),
                    fld(sf, 'PauseMinSeconds', sfp, 'Pause at home, min', null, 'num', { min: 0, unit: 'seconds' }),
                    fld(sf, 'PauseMaxSeconds', sfp, 'Pause at home, max', null, 'num', { min: 0, unit: 'seconds' }),
                    fld(sf, 'WindowFrom', sfp, 'Time window from', 'HH:mm game time, empty = always. May cross midnight', 'text', {}),
                    fld(sf, 'WindowTo', sfp, 'Time window to', 'HH:mm. A flight only starts if it is back before this time', 'text', {})
                ].filter(Boolean)));
                body.appendChild(sec('Shadow fleet: Defender', [
                    el('div', { class: 'cfg-note', text: 'Off (default): no flight starts when an attack would land before the fleet is back, and a flying fleet stays in the air when an attack lands before its return. On (trap): the Defender does not send the shadow fleet away but still saves resources and all other ships; a flying shadow fleet is called home so that it is there when the attack lands. Sleep-time fleetsaves still send everything away.' }),
                    fld(sf, 'DefenderOff', sfp, 'Trap: Defender leaves the shadow fleet alone', 'The fleet is away while the attacker looks and home when the attack lands', 'toggle')
                ].filter(Boolean)));
            } else {
                body.appendChild(sec('Shadow fleet', [
                    el('div', { class: 'cfg-warnbox' }, [el('b', { text: 'Deuterium reserve needed. ' }), document.createTextNode('Every flight burns fuel; keep deuterium on the celestial.')]),
                    el('div', { class: 'cfg-note', text: 'Not in your config yet (off). Add it to switch it on.' }),
                    addDefault(X, 'ShadowFleet', { Active: false, Celestial: { Galaxy: 1, System: 1, Position: 1, Type: 'Planet' }, Ships: { Recycler: 0 }, SpeedPercent: 10, MinBackInMinutes: 20, MaxBackInMinutes: 60, PauseMinSeconds: 30, PauseMaxSeconds: 180, DeuteriumFlights: 3, WindowFrom: '', WindowTo: '', DefenderOff: false }, 'Shadow fleet settings', null)
                ]));
            }
        }

        if (G && typeof G === 'object') {
            var gp = ['General'];
            if (G.LoginPause && typeof G.LoginPause === 'object') {
                var lpo = G.LoginPause, lpp = gp.concat('LoginPause');
                body.appendChild(sec('Login pause (protects the IP)', [
                    el('div', { class: 'cfg-note', text: 'After repeated failed logins (captcha etc.) the bot waits instead of trying again and again. The pause doubles with every further failure. The manual captcha page (/bot/captcha) stays usable; solving the captcha by hand ends the pause.' }),
                    fld(lpo, 'Active', lpp, 'Login pause active', null, 'toggle'),
                    fld(lpo, 'FailuresBeforePause', lpp, 'Failed logins before the first pause', null, 'num', { min: 1 }),
                    fld(lpo, 'PauseMinutes', lpp, 'First pause', 'Doubles with every further failure', 'num', { min: 1, unit: 'minutes' }),
                    fld(lpo, 'MaxPauseMinutes', lpp, 'Longest pause', null, 'num', { min: 1, unit: 'minutes' })
                ].filter(Boolean)));
            } else {
                body.appendChild(sec('Login pause (protects the IP)', [
                    el('div', { class: 'cfg-note', text: 'Not in your config yet - the defaults (pause after 2 failed logins, 30 minutes, doubling up to 120) are used. Add it to change them.' }),
                    addDefault(G, 'LoginPause', { Active: true, FailuresBeforePause: 2, PauseMinutes: 30, MaxPauseMinutes: 120 }, 'Login pause settings', null)
                ]));
            }
        }

        if (B && typeof B === 'object') {
            var bpath = ['Brain'];
            if (B.AutoMine && typeof B.AutoMine === 'object') {
                var am = B.AutoMine, amp = bpath.concat('AutoMine');
                body.appendChild(sec('AutoMine: energy planning', [
                    el('div', { class: 'cfg-note', text: 'Energy is built ahead of a mine or facility upgrade (Terraformer and others), so the upgrade does not run into an energy shortage. Solar satellites are built in batches. The other AutoMine settings stay on the Brain page.' }),
                    testingField(am, 'SolarSatelliteBatch', 20, amp, 'Satellites per batch', 'Solar satellites are built in multiples of this number (0 or 1 = one by one)', 'num', { min: 0 }),
                    testingField(am, 'EnergyReservePercent', 10, amp, 'Energy reserve in advance', 'Extra energy built before the upgrade that needs it, in percent of the missing energy', 'num', { min: 0, unit: '%' })
                ].filter(Boolean)));
            }
            if (B.AutoRepatriate && typeof B.AutoRepatriate === 'object' && B.AutoRepatriate.Reverse && typeof B.AutoRepatriate.Reverse === 'object') {
                var R = B.AutoRepatriate.Reverse, rp = bpath.concat('AutoRepatriate', 'Reverse');
                body.appendChild(sec('Reverse repatriation', [
                    el('div', { class: 'cfg-note', text: 'Fetches resources back out of the repatriate target when a build needs them. It never builds ships. It only starts if the free cargo space is large enough. It needs AutoRepatriate to be switched on on the Brain page.' }),
                    fld(R, 'Active', rp, 'Active', null, 'toggle'),
                    fld(R, 'ReturnFuelFactor', rp, 'Return fuel factor', null, 'num', { min: 0, step: 0.1 }),
                    fld(R, 'ReturnAllShips', rp, 'Return all ships', null, 'toggle'),
                    fld(R, 'PendingTtlMinutes', rp, 'Pending request valid for', null, 'num', { min: 0, unit: 'minutes' }),
                    fld(R, 'OutboundCapacityFactor', rp, 'Outbound capacity factor', null, 'num', { min: 0, step: 0.1 })
                ].concat(restRows(R, rp, ['Active', 'ReturnFuelFactor', 'ReturnAllShips', 'PendingTtlMinutes', 'OutboundCapacityFactor'])).filter(Boolean)));
            }
            if (B.KeepBoosters && typeof B.KeepBoosters === 'object') {
                brainSub(body, 'KeepBoosters', 'KeepBoosters', buildKeepBoosters, B, bpath);
            }
        }
        if ((!X || typeof X !== 'object') && (!G || typeof G !== 'object') && (!B || typeof B !== 'object'))
            body.appendChild(el('div', { class: 'cfg-note', text: 'The General and Brain blocks were not found in the config, so nothing can be shown here.' }));
    }

    var MESSENGER_HELP = {
        TelegramMessenger: 'Telegram bot: sends alerts (attacks, fleetsaves, sleep and wake-up, ...) and takes commands such as /ghost and /debrissave. Bot token and chat ID are stored in secrets/telegram; they show as ***** while Hide data is on.',
        DiscordWebhook: 'Mirrors every Telegram message to a Discord channel as an embed (send only, commands still go through Telegram). The webhook URL is stored in secrets/discord and shows as ***** while Hide data is on. The Defender also has its own Discord attack alert.'
    };
    function secretField(o, key, path, label, hint) {
        var orig = origAt(path.concat(key));
        var i = el('input', { type: 'text', value: o[key] == null ? '' : String(o[key]), autocomplete: 'off', placeholder: 'not set' });
        i.addEventListener('input', function () {
            var v = i.value.trim();
            if (v === '') { if (orig === undefined) delete o[key]; else o[key] = ''; }
            else o[key] = (typeof orig === 'number' && /^-?\d+$/.test(v)) ? Number(v) : v;
            i.classList.toggle('cfg-changed', JSON.stringify(o[key]) !== JSON.stringify(orig)); update();
        });
        sens(i);
        return withHelp(row(label, hint, i), fullKey(path, key));
    }
    function renderCommandPrompt(body) {
        var input = el('input', { type: 'text', class: 'cfg-cmd-input', placeholder: '/collect moon skip', spellcheck: 'false', autocomplete: 'off' });
        var out = el('div', { class: 'cfg-note', text: 'Check only - nothing is sent to the game or to Telegram.' });
        var list = el('div', { class: 'cfg-cmd-list' });
        var timer = null, seq = 0;
        function show(r) {
            out.textContent = '';
            out.style.color = r.ok ? 'var(--tb-ok, #2e7d32)' : (r.known ? 'var(--tb-bad, #c62828)' : '');
            out.appendChild(el('b', { text: (r.ok ? '✓ ' : r.known ? '✗ ' : '') + r.message }));
            if (r.syntax) out.appendChild(el('div', { class: 'cfg-note', text: r.syntax + (r.short ? '  -  ' + r.short : '') }));
            if (r.preview) out.appendChild(el('pre', { class: 'cfg-cmd-preview', text: r.preview }));
            else if (r.note) out.appendChild(el('div', { class: 'cfg-note', text: r.note }));
        }
        function check() {
            var mine = ++seq, fd = new URLSearchParams(); fd.set('line', input.value); fd.set('instance', instance || '');
            if (!input.value.trim()) { out.style.color = ''; out.textContent = 'Check only - nothing is sent to the game or to Telegram.'; return; }
            fetch(main.dataset.cmdcheck, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: fd.toString() })
                .then(function (r) { return r.json(); }).then(function (r) { if (mine === seq) show(r); })
                .catch(function () { if (mine === seq) out.textContent = 'The check failed (WebUI not reachable).'; });
        }
        input.addEventListener('input', function () { clearTimeout(timer); timer = setTimeout(check, 250); });
        input.addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); clearTimeout(timer); check(); } });
        body.appendChild(sec('Command line (check only)', [
            el('div', { class: 'cfg-note', text: 'Type a Telegram command to see whether the syntax is right. It is only checked, never executed. Click a command in the list to copy its example.' }),
            input, out, list
        ]));
        if (!main.dataset.cmdlist) return;
        fetch(main.dataset.cmdlist, { cache: 'no-store' }).then(function (r) { return r.json(); }).then(function (cmds) {
            var box = el('details', { class: 'cfg-inner' }, [el('summary', { text: 'All commands' })]);
            cmds.forEach(function (c) {
                var row1 = el('div', { class: 'cfg-cmd-row', title: c.syntax }, [el('code', { text: '/' + c.name }), document.createTextNode('  ' + c.description)]);
                row1.style.cursor = 'pointer';
                row1.addEventListener('click', function () { input.value = c.example; input.focus(); check(); });
                box.appendChild(row1);
            });
            list.appendChild(box);
        }).catch(function () { });
    }

    function renderMessengerPage(body) {
        renderCommandPrompt(body);
        function block1(name, title, d, build) {
            if (!d) {
                body.appendChild(sec(title, [el('div', { class: 'cfg-note', text: 'No "' + name + '" block found in the config, so nothing can be edited here.' })]));
                return;
            }
            var P = [name];
            var kids = [el('div', { class: 'cfg-note', text: MESSENGER_HELP[name] }), fld(d, 'Active', P, title + ' active', 'Switch it on to show and edit the options', 'toggle')];
            if (d.Active) build(d, P).forEach(function (x) { if (x) kids.push(x); });
            body.appendChild(sec(title, kids.filter(Boolean)));
        }
        block1('TelegramMessenger', 'Telegram', state.TelegramMessenger, function (d, P) {
            var out = [
                secretField(d, 'API', P, 'Bot token', 'From @BotFather'),
                secretField(d, 'ChatId', P, 'Chat ID', 'The chat the bot talks to'),
                fld(d, 'Logging', P, 'Log Telegram traffic', 'Write messages sent and received to the log', 'toggle')];
            var ping = d.TelegramAutoPing && typeof d.TelegramAutoPing === 'object' ? d.TelegramAutoPing : null;
            if (ping) {
                out.push(fld(ping, 'Active', P.concat('TelegramAutoPing'), 'Auto ping', 'Sends a regular "still alive" message', 'toggle'));
                out.push(fld(ping, 'EveryHours', P.concat('TelegramAutoPing'), 'Ping every', null, 'num', { min: 1, unit: 'hours' }));
            }
            var rest = Object.keys(d).filter(function (k) { return ['Active', 'API', 'ChatId', 'Logging', 'TelegramAutoPing', 'Load'].indexOf(k) < 0; });
            if (rest.length) out.push(genericObject(d, rest, P));
            return out;
        });
        block1('DiscordWebhook', 'Discord', state.DiscordWebhook, function (d, P) {
            var out = [
                secretField(d, 'Url', P, 'Webhook URL', 'Channel settings > Integrations > Webhooks'),
                fld(d, 'Username', P, 'Display name', 'Name shown on the Discord posts', 'text')];
            var rest = Object.keys(d).filter(function (k) { return ['Active', 'Url', 'Username', 'Load'].indexOf(k) < 0; });
            if (rest.length) out.push(genericObject(d, rest, P));
            return out;
        });
    }

    function has(o, k) { return o && Object.prototype.hasOwnProperty.call(o, k); }
    function fullKey(path, k) {
        return (COMBINED ? path : [block].concat(path)).concat(k).join('.');
    }
    var LANG = main.dataset.lang || 'en';
    var UI_TEXT = { de: { 'Example: ': 'Beispiel: ', 'Explain this option': 'Diese Option erklaeren', 'Show all help': 'Alle Hilfen zeigen', 'Language': 'Sprache' } };
    function uiText(s) { return (UI_TEXT[LANG] && UI_TEXT[LANG][s]) || s; }
    function helpFor(full) {
        var all = window.CFG_HELP || {};
        return helpIn(all[LANG], full) || (LANG !== 'en' ? helpIn(all.en, full) : null);
    }
    function helpIn(H, full) {
        if (!H) return null;
        var parts = full.split('.'), k = parts[parts.length - 1], rel = parts.slice(1).join('.');
        var bl = H[parts[0]] || {}, cm = H['*'] || {};
        if (bl[rel]) return bl[rel];
        var best = '';
        Object.keys(bl).forEach(function (w) {
            if (w.charAt(w.length - 1) === '*' && rel.indexOf(w.slice(0, -1)) === 0 && w.length > best.length) best = w;
        });
        if (best) return bl[best];
        if (bl[k]) return bl[k];
        var last2 = parts.slice(-2).join('.');
        return cm[rel] || cm[last2] || cm[k] || null;
    }
    var helpAll = false;
    function withHelp(r, full) {
        var h = r && full ? helpFor(full) : null;
        if (!h) return r;
        var box = el('div', { class: 'cfg-opt-help' }, [el('span', { text: h.t }), h.e ? el('span', { class: 'eg' }, [el('b', { text: uiText('Example: ') }), h.e]) : null]);
        var q = el('button', { type: 'button', class: 'cfg-q', title: uiText('Explain this option'), text: '?' });
        q.addEventListener('click', function () { box.classList.toggle('open'); q.classList.toggle('on', box.classList.contains('open')); });
        var lab = r.querySelector('.cfg-lab b');
        if (lab) lab.appendChild(q);
        r.appendChild(box);
        return r;
    }
    function helpLabel() {
        var cb = el('input', { type: 'checkbox' });
        cb.checked = helpAll;
        cb.addEventListener('change', function () { helpAll = cb.checked; root.classList.toggle('cfg-help-all', helpAll); });
        return el('span', { class: 'cfg-masklbl' }, [el('label', { class: 'cfg-masklbl' }, [cb, ' ' + uiText('Show all help')])]);
    }

    function fld(o, k, path, label, hint, kind, opts) {
        if (!has(o, k)) return null;
        var p = path.concat(k), c;
        opts = opts || {};
        if (kind === 'toggle') c = toggle(o, k, p);
        else if (kind === 'select') c = select(o, k, p, opts.options);
        else if (kind === 'text') c = text(o, k, p, opts.validate);
        else if (kind === 'intlist') c = intList(o, k, p);
        else if (kind === 'strlist') c = strList(o, k, p);
        else c = num(o, k, p, opts);
        if (opts.sens) sens(c);
        return withHelp(row(label, hint, c, opts.unit), fullKey(path, k));
    }
    function intList(o, k, p) {
        var i = el('input', { type: 'text', value: (o[k] || []).join(', '), placeholder: 'e.g. 12, 345' });
        i.addEventListener('input', function () {
            o[k] = i.value.split(',').map(function (x) { return x.trim(); }).filter(function (x) { return /^\d+$/.test(x); }).map(Number);
            i.classList.toggle('cfg-changed', JSON.stringify(o[k]) !== JSON.stringify(origAt(p))); update();
            if (allyBox && allyBox.redraw) allyBox.redraw();
        });
        return i;
    }
    function strList(o, k, p) {
        var i = el('input', { type: 'text', value: (o[k] || []).join(', '), placeholder: 'e.g. LargeCargo, SmallCargo' });
        i.addEventListener('input', function () {
            o[k] = i.value.split(',').map(function (x) { return x.trim(); }).filter(Boolean);
            i.classList.toggle('cfg-changed', JSON.stringify(o[k]) !== JSON.stringify(origAt(p))); update();
        });
        return i;
    }
    function listTable(arr, cols, makeNew, addLabel, base) {
        base = base || [];
        var tbody = el('tbody');
        function draw() {
            tbody.innerHTML = '';
            arr.forEach(function (item, idx) {
                var tr = el('tr');
                cols.forEach(function (c) {
                    var ctl = c.type === 'sel' ? select(item, c.key, base.concat([idx, c.key]), c.opts) : num(item, c.key, base.concat([idx, c.key]), { min: c.min });
                    if (item[c.key] === undefined && c.type !== 'sel') ctl.value = '';
                    if (c.sens) sens(ctl);
                    tr.appendChild(el('td', null, [ctl]));
                });
                tr.appendChild(el('td', null, [el('button', { type: 'button', class: 'cfg-btn sm', text: '✕', onclick: function () { arr.splice(idx, 1); draw(); update(); } })]));
                tbody.appendChild(tr);
            });
            applyMask();
        }
        draw();
        return el('div', { style: 'overflow-x:auto' }, [
            el('table', { class: 'cfg-table' }, [
                el('thead', null, [el('tr', null, cols.map(function (c) { return el('th', { text: c.label }); }).concat([el('th')]))]), tbody]),
            el('button', { type: 'button', class: 'cfg-btn sm', text: addLabel, onclick: function () { arr.push(makeNew()); draw(); update(); } })
        ]);
    }
    var SHIP_CHOICES = ['SmallCargo', 'LargeCargo', 'Recycler', 'Pathfinder', 'LightFighter', 'HeavyFighter', 'Cruiser', 'Battleship', 'Battlecruiser', 'Bomber', 'Destroyer', 'Deathstar', 'Reaper', 'ColonyShip', 'EspionageProbe'];
    function shipPicker(o, k, p) {
        if (!o[k] || typeof o[k] !== 'object' || Array.isArray(o[k])) o[k] = {};
        var sel = o[k];
        var tbody = el('tbody');
        SHIP_CHOICES.forEach(function (name) {
            var on = Object.prototype.hasOwnProperty.call(sel, name);
            var cb = el('input', { type: 'checkbox' });
            cb.checked = on;
            var amount = el('input', { type: 'number', min: 0, step: 1, value: on ? sel[name] : 0, placeholder: '0 = all' });
            amount.disabled = !on;
            function mark() { var ch = JSON.stringify(sel) !== JSON.stringify(origAt(p) || {}); amount.classList.toggle('cfg-changed', ch); update(); }
            cb.addEventListener('change', function () {
                if (cb.checked) sel[name] = Number(amount.value) || 0; else delete sel[name];
                amount.disabled = !cb.checked; mark();
            });
            amount.addEventListener('input', function () { if (cb.checked) { sel[name] = Math.max(0, Number(amount.value) || 0); mark(); } });
            tbody.appendChild(el('tr', null, [el('td', null, [cb]), el('td', { text: name }), el('td', null, [amount])]));
        });
        return el('div', { style: 'overflow-x:auto' }, [
            el('table', { class: 'cfg-table' }, [el('thead', null, [el('tr', null, [el('th', { text: 'Fly' }), el('th', { text: 'Ship' }), el('th', { text: 'Amount (0 = all)' })])]), tbody])]);
    }
    var COORD_COLS = [
        { key: 'Galaxy', label: 'Galaxy', min: 1, sens: true }, { key: 'System', label: 'System', min: 1, sens: true },
        { key: 'Position', label: 'Position', min: 1, sens: true }, { key: 'Type', label: 'Type', type: 'sel', opts: ['Planet', 'Moon'] }];

    function renderAutoFarm(body) {
        var d = state, P = [];
        function group(title, kids, pill) { body.appendChild(sec(title, kids.filter(Boolean), pill)); }
        body.appendChild(el('div', { class: 'cfg-note cfg-toptip', text: 'Escort = the warships that go along with the cargo ships. The escort declares the fleet composition for attacks on defended targets (see "Escort and simulation"). Click the ? next to an option for a plain explanation, or switch on "Show all help" at the top.' }));
        function fold(title, kids) {
            var det = el('details', { class: 'cfg-sec cfg-more' }, [el('summary', { text: title })].concat(kids.filter(Boolean)));
            if (openSecs[title]) det.open = true;
            det.addEventListener('toggle', function () { openSecs[title] = det.open; });
            body.appendChild(det);
        }

        body.appendChild(lossPanel());
        group('General', [fld(d, 'Active', P, 'AutoFarm active', 'Master switch', 'toggle')]);

        var origins = [];
        if (Array.isArray(d.Origin)) origins.push(listTable(d.Origin, COORD_COLS, function () { return { Galaxy: 1, System: 1, Position: 1, Type: 'Planet' }; }, '+ Add origin', ['Origin']));
        group('Origins', origins.length ? [el('div', { class: 'cfg-note', text: 'Celestials the fleets start from.' })].concat(origins) : []);

        var scan = [];
        if (Array.isArray(d.ScanRange)) scan.push(withHelp(row('Scan range', 'Galaxy / system ranges',
            listTable(d.ScanRange, [{ key: 'Galaxy', label: 'Galaxy', min: 1, sens: true }, { key: 'StartSystem', label: 'From system', min: 1, sens: true }, { key: 'EndSystem', label: 'To system', min: 1, sens: true }],
                function () { return { Galaxy: 1, StartSystem: 1, EndSystem: 499 }; }, '+ Add range', ['ScanRange'])), 'AutoFarm.ScanRange'));
        scan.push(fld(d, 'SystemRange', P, 'System radius', 'Around the origin; replaces the scan range (0 = off)', 'num', { min: 0, sens: true }));
        if (Array.isArray(d.Exclude)) scan.push(withHelp(row('Excluded coordinates', 'Never touched',
            listTable(d.Exclude, COORD_COLS, function () { return { Galaxy: 1, System: 1, Position: 1, Type: 'Planet' }; }, '+ Add exclusion', ['Exclude'])), 'AutoFarm.Exclude'));
        scan.push(fld(d, 'ExcludeMoons', P, 'Skip moons', null, 'toggle'));
        scan.push(fld(d, 'DistanceBasedScanOrder', P, 'Nearest systems first', null, 'toggle'));
        scan.push(fld(d, 'ScanBlockSize', P, 'Scan block size', 'Stops the scan after this many scanned systems and attacks with the targets found so far; the next cycle continues where it stopped. Keeps a cycle short in dense or empty areas. 0 = off (scan the whole range first).', 'num', { min: 0 }));
        scan.push(fld(d, 'TargetsProbedBeforeAttack', P, 'Targets probed before attack', 'Upper limit of espionage probes per cycle; once reached, the scan pauses and the attacks run. 0 = no limit.', 'num', { min: 0 }));
        scan.push(fld(d, 'SequentialMode', P, 'Sequential mode', 'Slow, careful mode: sends probe(s), waits until their report is evaluated (and attacked if worthwhile), and only then probes the next target. Fill slots is ignored while on.', 'toggle'));
        scan.push(fld(d, 'SequentialModeProbesPerCycle', P, 'Probes per cycle (sequential)', 'How many targets are probed at once in sequential mode (minimum 1). Only used when Sequential mode is on.', 'num', { min: 1 }));
        scan.push(fld(d, 'StopAfterFullScan', P, 'Stop after one full pass', null, 'toggle'));
        fold('Scan area', scan);

        fold('Target choice', [
            fld(d, 'MinimumResources', P, 'Minimum loot', 'To attack a target', 'num', { min: 0 }),
            fld(d, 'MinimumResourcesDefended', P, 'Minimum loot (defended)', null, 'num', { min: 0 }),
            fld(d, 'MinimumPlayerRank', P, 'Minimum player rank', 'Ignore players ranked better than this', 'num', { min: 0 }),
            fld(d, 'MaxFlightTime', P, 'Max flight time', '0 = no limit', 'num', { min: 0, unit: 'seconds' }),
            fld(d, 'MinLootFuelRatio', P, 'Min loot / fuel ratio', null, 'num', { min: 0, step: 0.1 }),
            fld(d, 'PreferedResource', P, 'Preferred resource', 'Sorts targets by it', 'select', { options: ['', 'Metal', 'Crystal'] }),
            fld(d, 'AttackDefendedTargets', P, 'Attack defended targets', 'Needs the escort simulation', 'toggle'),
            fld(d, 'MaxDefensePoints', P, 'Max defence points', null, 'num', { min: 0 }),
            fld(d, 'MinLootPerDefensePoint', P, 'Min loot per defence point', null, 'num', { min: 0 }),
            fld(d, 'KeepReportFor', P, 'Report valid for', null, 'num', { min: 0, unit: 'minutes' })
        ]);

        if (d.Blacklist && typeof d.Blacklist === 'object') {
            var B = d.Blacklist, bp = ['Blacklist'];
            fold('Blacklist', [
                fld(B, 'Active', bp, 'Blacklist active', null, 'toggle'),
                fld(B, 'ResetAfterHours', bp, 'Reset after', null, 'num', { min: 0, unit: 'hours' }),
                fld(B, 'UnprofitableResetAfterHours', bp, 'Unprofitable: reset after', null, 'num', { min: 0, unit: 'hours' }),
                fld(B, 'PermanentResetAfterHours', bp, 'Permanent: reset after', null, 'num', { min: 0, unit: 'hours' })
            ]);
        }

        if (d.AllianceWhitelist && typeof d.AllianceWhitelist === 'object') {
            var W = d.AllianceWhitelist, wp = ['AllianceWhitelist'];
            var warn = null;
            if (W.Active && (!W.ServerHost || /NNN|xx/i.test(W.ServerHost)))
                warn = el('div', { class: 'cfg-checks' }, [el('div', { class: 'wn', text: '⚠ Active, but the server host looks like a placeholder - nothing will be filtered.' })]);
            fold('Alliance whitelist', [
                fld(W, 'Active', wp, 'Protect own alliance', 'Never spy or attack members', 'toggle'),
                fld(W, 'ServerHost', wp, 'Server host', 'Host of your universe', 'text', { sens: true }),
                el('div', { class: 'cfg-note', text: 'Your own alliance ID is neither shown nor editable in the web UI. Set it directly in the config file (secrets/alliance).' }),
                fld(W, 'AdditionalAllianceIds', wp, 'Partner alliance IDs', 'Comma separated (e.g. NAP partners)', 'intlist', { sens: true }),
                allyNamesBox(W),
                warn
            ]);
        } else if (d.AllianceWhitelist === undefined) {
            body.appendChild(el('div', { class: 'cfg-placeholder-card' }, [el('h3', { text: 'Alliance whitelist' }), el('div', { text: 'Not shown here: it is stored in secrets/ or not present. Edit it directly in the config file.' })]));
        }

        fold('Probes and slots', [
            fld(d, 'NumProbes', P, 'Probes per spy mission', null, 'num', { min: 1 }),
            fld(d, 'BuildProbes', P, 'Build missing probes', null, 'toggle'),
            fld(d, 'FillSlots', P, 'Keep all slots busy', null, 'toggle'),
            fld(d, 'FillSlotsMaxProbeWaitSec', P, 'Max wait for probe reports', null, 'num', { min: 0, unit: 'seconds' }),
            fld(d, 'MaxSlots', P, 'Max slots for AutoFarm', null, 'num', { min: 0 }),
            fld(d, 'SlotsToLeaveFree', P, 'Slots to leave free', null, 'num', { min: 0 })
        ]);

        fold('Cargo and fleet', [
            fld(d, 'FleetSpeed', P, 'Fleet speed', '10-100', 'num', { min: 10, max: 100, step: 10, unit: '%' }),
            fld(d, 'CargoType', P, 'Cargo ship', null, 'select', { options: ['SmallCargo', 'LargeCargo', 'Pathfinder'] }),
            fld(d, 'MinCargosToSend', P, 'Min cargo ships per attack', null, 'num', { min: 0 }),
            fld(d, 'MinCargosToKeep', P, 'Cargo ships kept at home', null, 'num', { min: 0 }),
            fld(d, 'CargoSurplusPercentage', P, 'Extra cargo', 'On top of the calculated amount', 'num', { min: 0, unit: '%' }),
            fld(d, 'CargoReservePercent', P, 'Cargo reserve', null, 'num', { min: 0, max: 100, unit: '%' }),
            fld(d, 'RecyclerReservePercent', P, 'Recycler reserve', null, 'num', { min: 0, max: 100, unit: '%' }),
            fld(d, 'BuildCargos', P, 'Build missing cargo ships', null, 'toggle'),
            fld(d, 'MaxCargoLossPercent', P, 'Max accepted cargo loss', null, 'num', { min: 0, max: 100, unit: '%' }),
            fld(d, 'FleetToDebrisPercent', P, 'Fleet loss that becomes debris', null, 'num', { min: 0, max: 100, unit: '%' }),
            fld(d, 'AutoHarvestDebrisAfterAttack', P, 'Harvest debris after attack', null, 'toggle')
        ]);

        var AF_OPT = [
            { k: 'MaxReportAgeMinutesForAttack', label: 'Max report age for attack', hint: 'Older reports are not attacked (0 = off)', kind: 'num', o: { min: 0, unit: 'minutes' }, def: 120 },
            { k: 'MaxAttackDelaySec', label: 'Max attack delay', hint: 'Longest postponement to fit the timing', kind: 'num', o: { min: 0, unit: 'seconds' }, def: 600 },
            { k: 'DebrisCreditPercent', label: 'Debris credit', hint: 'Share of debris counted as profit', kind: 'num', o: { min: 0, max: 100, unit: '%' }, def: 80 },
            { k: 'MinDebrisToHarvest', label: 'Min debris to harvest', hint: 'After recycler fuel', kind: 'num', o: { min: 0 }, def: 0 },
            { k: 'MinSpeedPercentForProfitRetry', label: 'Min speed for profit re-check', hint: null, kind: 'num', o: { min: 10, max: 100, step: 10, unit: '%' }, def: 50 },
            { k: 'GlobalSpacing', label: 'Global spacing', hint: 'Spread several bot instances over different start points', kind: 'toggle', def: false },
            { k: 'ReportQueryLimitForNonActionable', label: 'Report query limit (not attackable)', hint: 'Per run', kind: 'num', o: { min: 0 }, def: 3 },
            { k: 'RepeatedLogIntervalMinutes', label: 'Repeat log interval', hint: 'Identical log lines at most this often (0 = always)', kind: 'num', o: { min: 0, unit: 'minutes' }, def: 360 }
        ];
        var DEUT_DEFAULTS = { ReduceDeuterium: false, ReduceDeuteriumMinSpeedPercent: 30, DeuteriumGuard: true, DeuteriumGuardMin: 10000000, DeuteriumGuardOverride: false, UseSpeedFleet: false };
        Object.keys(DEUT_DEFAULTS).forEach(function (k) { if (!has(d, k)) d[k] = DEUT_DEFAULTS[k]; });
        body.appendChild(el('details', { class: 'cfg-sec cfg-more' }, [el('summary', { text: 'Deuterium and speed fleet' })].concat([
            el('div', { class: 'cfg-note', text: 'Save deuterium: attack fleet and recyclers fly as slowly as sensible (never below the lowest speed). Low-deuterium guard: when a start celestial has less deuterium than the minimum, attacks on defended targets from there are paused and the speed fleet is switched off. The AutoFarm page shows the stock and a warning.' }),
            fld(d, 'UseSpeedFleet', P, 'Speed fleet (UseSpeedFleet)', 'Use Pathfinders as cargo ships; switched off automatically while the low-deuterium guard is active', 'toggle'),
            fld(d, 'ReduceDeuterium', P, 'Save deuterium', 'Attack fleet and recyclers fly as slowly as sensible (flight time limit kept, recycler arrives right behind the fleet)', 'toggle'),
            fld(d, 'ReduceDeuteriumMinSpeedPercent', P, 'Lowest speed when saving deuterium', 'Never below 30 %', 'num', { min: 30, max: 100, step: 10, unit: '%' }),
            fld(d, 'DeuteriumGuard', P, 'Low-deuterium guard', 'Below the minimum on the start celestial: no speed fleet, no attacks on defended targets', 'toggle'),
            fld(d, 'DeuteriumGuardMin', P, 'Guard: minimum deuterium', null, 'num', { min: 0 }),
            fld(d, 'DeuteriumGuardOverride', P, 'Guard: bridge manually', 'On = ignore the guard and keep farming with low deuterium', 'toggle')
        ].filter(Boolean))));

        var esc = [
            fld(d, 'TargetCombatTechBonus', P, 'Target combat tech bonus', 'Safety margin in simulations', 'num', { min: 0 }),
            fld(d, 'EscortShipTypes', P, 'Escort ships', 'Comma separated, in order of preference', 'text'),
            fld(d, 'EscortFallbackShipTypes', P, 'Fallback escort ships', null, 'text'),
            fld(d, 'EscortShipsStep', P, 'Escort step size', null, 'num', { min: 1 }),
            fld(d, 'MaxEscortShips', P, 'Max escort ships', null, 'num', { min: 0 }),
            fld(d, 'MaxEscortFleetPercent', P, 'Max share of the fleet', null, 'num', { min: 0, max: 100, unit: '%' }),
            fld(d, 'EscortReservePercent', P, 'Escort reserve', null, 'num', { min: 0, max: 100, unit: '%' }),
            fld(d, 'MaxEscortLossPercent', P, 'Max escort loss (% of fleet value)', 'Highest simulated loss, as a share of the value of the whole attack fleet. 0 = limit OFF (any loss is accepted)', 'num', { min: 0, max: 100, unit: '%' }),
            fld(d, 'EscortLossWeight', P, 'Loss weight vs. fuel', '>1 prefers a larger escort', 'num', { min: 0, step: 0.5 }),
            fld(d, 'MinWinRate', P, 'Min win rate', '0.9 = 90 %', 'num', { min: 0, max: 1, step: 0.05 }),
            fld(d, 'SimulationRuns', P, 'Battles per check', null, 'num', { min: 1 }),
            fld(d, 'EscortMaxIterations', P, 'Search: max iterations', null, 'num', { min: 1 }),
            fld(d, 'EscortBisectionIterations', P, 'Search: bisection iterations', null, 'num', { min: 1 }),
            fld(d, 'EscortFineTuneMaxSims', P, 'Search: fine-tune simulations', null, 'num', { min: 1 })
        ];
        (function () {
            var capTypes = [];
            [d.EscortShipTypes, d.EscortFallbackShipTypes].forEach(function (lst) {
                String(lst || '').split(',').forEach(function (t) { t = t.trim(); if (t && capTypes.indexOf(t) < 0) capTypes.push(t); });
            });
            Object.keys(d.EscortShipCaps && typeof d.EscortShipCaps === 'object' ? d.EscortShipCaps : {}).forEach(function (t) { if (capTypes.indexOf(t) < 0) capTypes.push(t); });
            capTypes.forEach(function (t) {
                var have = d.EscortShipCaps && typeof d.EscortShipCaps === 'object' && d.EscortShipCaps[t] != null;
                var i = el('input', { type: 'number', min: 0, step: 1, value: have ? d.EscortShipCaps[t] : '', placeholder: 'default' });
                var cpath = P.concat('EscortShipCaps', t);
                i.addEventListener('input', function () {
                    if (!d.EscortShipCaps || typeof d.EscortShipCaps !== 'object') d.EscortShipCaps = {};
                    if (i.value === '') delete d.EscortShipCaps[t]; else d.EscortShipCaps[t] = Number(i.value);
                    if (!Object.keys(d.EscortShipCaps).length && origAt(P.concat('EscortShipCaps')) === undefined) delete d.EscortShipCaps;
                    i.classList.toggle('cfg-changed', JSON.stringify(i.value === '' ? undefined : Number(i.value)) !== JSON.stringify(origAt(cpath)));
                    update();
                });
                esc.push(withHelp(row('Cap: ' + t, 'Absolute cap for this ship type (empty = Max escort ships)', i), 'AutoFarm.EscortCap'));
            });
        })();
        var escDet = el('details', { class: 'cfg-sec cfg-more' }, [el('summary', { text: 'Escort and simulation (defended targets only)' }), el('div', { class: 'cfg-note', text: 'The escort (warships) decides the fleet composition for attacks on defended targets: the bot simulates the fight and adds warships to the cargo ships until the attack is safe enough.' })].concat(esc.filter(Boolean)));
        if (escOpen) escDet.open = true;
        escDet.addEventListener('toggle', function () { escOpen = escDet.open; });
        body.appendChild(escDet);

        fold('Timing', [
            fld(d, 'CheckIntervalMin', P, 'Run interval, min', null, 'num', { min: 0, unit: 'minutes' }),
            fld(d, 'CheckIntervalMax', P, 'Run interval, max', null, 'num', { min: 0, unit: 'minutes' })
        ]);

        var known = ['Active', 'Origin', 'ScanRange', 'SystemRange', 'Exclude', 'ExcludeMoons', 'DistanceBasedScanOrder', 'ScanBlockSize', 'TargetsProbedBeforeAttack', 'SequentialMode', 'SequentialModeProbesPerCycle', 'StopAfterFullScan', 'MinimumResources', 'MinimumResourcesDefended', 'MinimumPlayerRank', 'MaxFlightTime', 'MinLootFuelRatio', 'PreferedResource', 'AttackDefendedTargets', 'MaxDefensePoints', 'MinLootPerDefensePoint', 'KeepReportFor', 'MaxReportAgeMinutesForAttack', 'MaxAttackDelaySec', 'DebrisCreditPercent', 'MinDebrisToHarvest', 'MinSpeedPercentForProfitRetry', 'ReduceDeuterium', 'ReduceDeuteriumMinSpeedPercent', 'DeuteriumGuard', 'DeuteriumGuardMin', 'DeuteriumGuardOverride', 'GlobalSpacing', 'ReportQueryLimitForNonActionable', 'RepeatedLogIntervalMinutes', 'Blacklist', 'AllianceWhitelist', 'NumProbes', 'BuildProbes', 'FillSlots', 'FillSlotsMaxProbeWaitSec', 'MaxSlots', 'SlotsToLeaveFree', 'FleetSpeed', 'CargoType', 'MinCargosToSend', 'MinCargosToKeep', 'CargoSurplusPercentage', 'CargoReservePercent', 'RecyclerReservePercent', 'BuildCargos', 'UseSpeedFleet', 'MaxCargoLossPercent', 'FleetToDebrisPercent', 'AutoHarvestDebrisAfterAttack', 'TargetCombatTechBonus', 'EscortShipTypes', 'EscortFallbackShipTypes', 'EscortShipCaps', 'EscortShipsStep', 'MaxEscortShips', 'MaxEscortFleetPercent', 'EscortReservePercent', 'EscortMaxIterations', 'EscortBisectionIterations', 'EscortFineTuneMaxSims', 'MaxEscortLossPercent', 'EscortLossWeight', 'MinWinRate', 'SimulationRuns', 'CheckIntervalMin', 'CheckIntervalMax'];
        var rest = Object.keys(d).filter(function (k) { return known.indexOf(k) < 0; });
        if (rest.length) {
            var det = el('details', { class: 'cfg-sec cfg-more' }, [el('summary', { text: 'More settings (' + rest.length + ')' }), genericObject(d, rest, [])]);
            body.appendChild(det);
        }

        var optRows = [el('div', { class: 'cfg-note', text: 'Advanced options: deliberately kept at the end of the page and not part of the normal setup. The built-in default values are enough for almost everyone, so there is no need to touch these.' })].concat(AF_OPT.map(function (x) { return fld(d, x.k, P, x.label, x.hint, x.kind, x.o); }));
        var missingOpt = AF_OPT.filter(function (x) { return !has(d, x.k); }).map(function (x) { return { k: x.k, label: x.label, def: x.def }; });
        if (missingOpt.length) {
            var pick = el('select');
            missingOpt.forEach(function (m) { pick.appendChild(el('option', { value: m.k, text: m.label })); });
            optRows.push(row('Missing options (' + missingOpt.length + ')', 'Not in your config yet - the default is used until you add one',
                el('span', null, [pick, ' ', el('button', { type: 'button', class: 'cfg-btn sm', text: '+ Add', onclick: function () {
                    var m = missingOpt.filter(function (x) { return x.k === pick.value; })[0];
                    if (!m) return;
                    d[m.k] = JSON.parse(JSON.stringify(m.def));
                    openSecs['Advanced options'] = true;
                    update(); render();
                } })])));
        }
        fold('Advanced options', optRows);
    }

    function genericObject(obj, keys, path, noHelp) {
        var wh = noHelp ? function (r) { return r; } : withHelp;
        var wrap = el('div', { class: 'cfg-generic-group' });
        keys.forEach(function (k) {
            var v = obj[k], p = path.concat(k);
            var sn = SENS_RE.test(k) ? sens : function (x) { return x; };
            if (typeof v === 'boolean') wrap.appendChild(wh(row(k, null, toggle(obj, k, p)), fullKey(path, k)));
            else if (typeof v === 'number') wrap.appendChild(wh(row(k, null, sn(num(obj, k, p, { step: 'any' }))), fullKey(path, k)));
            else if (typeof v === 'string') wrap.appendChild(wh(row(k, null, sn(text(obj, k, p))), fullKey(path, k)));
            else if (v && typeof v === 'object' && !Array.isArray(v)) {
                wrap.appendChild(el('div', { class: 'cfg-gname', text: k }));
                var gh = noHelp ? null : helpFor(fullKey(path, k));
                if (gh) wrap.appendChild(el('div', { class: 'cfg-note', text: gh.t }));
                wrap.appendChild(genericObject(v, Object.keys(v), p, noHelp || !!gh));
            } else wrap.appendChild(wh(row(k, 'Array/complex - edit in the JSON tab', el('code', { text: JSON.stringify(v) })), fullKey(path, k)));
        });
        return wrap;
    }
    function renderGeneric(body) {
        body.appendChild(sec(block, [genericObject(state, Object.keys(state), [])]));
    }

    function pretty(k) { return k.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/([A-Za-z])(\d)/g, '$1 $2'); }
    function newCoord() { return { Galaxy: 1, System: 1, Position: 1, Type: 'Planet' }; }
    function coordRows(o, p) {
        if (!o || typeof o !== 'object') return [];
        return [
            fld(o, 'Galaxy', p, 'Galaxy', null, 'num', { min: 1, sens: true }),
            fld(o, 'System', p, 'System', null, 'num', { min: 1, sens: true }),
            fld(o, 'Position', p, 'Position', null, 'num', { min: 1, sens: true }),
            fld(o, 'Type', p, 'Type', null, 'select', { options: ['Planet', 'Moon'] })
        ].filter(Boolean);
    }
    function coordList(o, k, label, hint, addLabel, p) {
        if (!Array.isArray(o[k])) return null;
        return withHelp(row(label, hint || null, listTable(o[k], COORD_COLS, newCoord, addLabel || '+ Add celestial', [k])), p ? fullKey(p, k) : null);
    }
    function intervalRows(o, p) {
        return [
            fld(o, 'CheckIntervalMin', p, 'Run interval, min', null, 'num', { min: 0, unit: 'minutes' }),
            fld(o, 'CheckIntervalMax', p, 'Run interval, max', 'A random time between min and max is used', 'num', { min: 0, unit: 'minutes' })
        ].filter(Boolean);
    }
    function autoRows(o, p, keys) {
        return keys.map(function (k) {
            if (!has(o, k)) return null;
            return typeof o[k] === 'boolean' ? fld(o, k, p, pretty(k), null, 'toggle') : fld(o, k, p, pretty(k), null, 'num', { min: 0, step: 'any' });
        }).filter(Boolean);
    }
    function numKeys(o, re) { return Object.keys(o).filter(function (k) { return re.test(k) && typeof o[k] === 'number'; }); }
    function boolKeys(o, re) { return Object.keys(o).filter(function (k) { return re.test(k) && typeof o[k] === 'boolean'; }); }
    function sec2(title, kids) { return el('details', { class: 'cfg-inner' }, [el('summary', { text: title })].concat(kids.filter(Boolean))); }
    function inner(key, title, kids) {
        var det = sec2(title, kids);
        if (openSecs[key]) det.open = true;
        det.addEventListener('toggle', function () { openSecs[key] = det.open; });
        return det;
    }
    function gname(t) { return el('div', { class: 'cfg-gname', text: t }); }
    function restRows(o, p, handled) {
        var rest = Object.keys(o).filter(function (k) { return handled.indexOf(k) < 0; });
        return rest.length ? [gname('More settings'), genericObject(o, rest, p)] : [];
    }
    function transportRow(o, p) {
        if (!o.Transports || typeof o.Transports !== 'object') return null;
        return fld(o.Transports, 'Active', p.concat('Transports'), 'Fetch missing resources by transport', 'Allow this worker to move resources from other celestials when it lacks them', 'toggle');
    }

    var LF_NAMES = {"1": ["Intergalactic Envoys", "Volcanic Batteries", "Catalyser Technology", "Heat Recovery"], "2": ["High Performance Extractors", "Acoustic Scanning", "Plasma Drive", "Sulphide Process"], "3": ["Fusion Drives", "High Energy Pump Systems", "Efficiency Module", "Psionic Network"], "4": ["Stealth Field Generator", "Cargo Hold Expansion Civilian Ships", "Depot AI", "Telekinetic Tractor Beam"], "5": ["Orbital Den", "Magma Powered Production", "General Overhaul Light Fighter", "Enhanced Sensor Technology"], "6": ["Research AI", "Geothermal Power Plants", "Automated Transport Lines", "Neuromodal Compressor"], "7": ["High Performance Terraformer", "Depth Sounding", "Improved Drone AI", "Neuro Interface"], "8": ["Enhanced Production Technologies", "Ion Crystal Enhancement Heavy Fighter", "Experimental Recycling Technology", "Interplanetary Analysis Network"], "9": ["Light Fighter Mk II", "Improved Stellarator", "General Overhaul Cruiser", "Overclocking Heavy Fighter"], "10": ["Cruiser Mk II", "Hardened Diamond Drill Heads", "Slingshot Autopilot", "Telekinetic Drive"], "11": ["Improved Lab Technology", "Seismic Mining Technology", "High Temperature Superconductors", "Sixth Sense"], "12": ["Plasma Terraformer", "Magma Powered Pump Systems", "General Overhaul Battleship", "Psychoharmoniser"], "13": ["Low Temperature Drives", "Ion Crystal Modules", "Artificial Swarm Intelligence", "Efficient Swarm Intelligence"], "14": ["Bomber Mk II", "Optimised Silo Construction Method", "General Overhaul Battlecruiser", "Overclocking Large Cargo"], "15": ["Destroyer Mk II", "Diamond Energy Transmitter", "General Overhaul Bomber", "Gravitation Sensors"], "16": ["Battlecruiser Mk II", "Obsidian Shield Reinforcement", "General Overhaul Destroyer", "Overclocking Battleship"], "17": ["Robot Assistants", "Rune Shields", "Experimental Weapons Technology", "Psionic Shield Matrix"], "18": ["Supercomputer", "Rocktal Collector Enhancement", "Mechan General Enhancement", "Kaelesh Discoverer Enhancement"]};
    var LF_RACES = ['Humans', "Rock'tal", 'Mechas', 'Kaelesh'];
    var LF_IMG = main.dataset.lfimg || '/img/lf/';
    function lfIndex(slot) { return (Math.floor(slot / 10) - 1) * 6 + (slot % 10); }
    function lfNames(slot) { return LF_NAMES[String(lfIndex(slot))] || ['?', '?', '?', '?']; }
    var LF_FILE = ['Humans', 'Rocktal', 'Mechas', 'Kaelesh'];
    function lfImgs(slot) {
        var idx = lfIndex(slot), wrap = el('span', { class: 'cfg-lfimgs' });
        if (!(idx >= 1 && idx <= 18)) return wrap;
        for (var r = 1; r <= 4; r++) {
            var id = 10000 + r * 1000 + 200 + idx;
            var cand = [LF_FILE[r - 1] + '-research-' + idx + '-200px.jpg', id + '.png', id + '.jpg'];
            var im = el('img', { class: 'cfg-lfimg', width: 26, height: 26, alt: '', loading: 'lazy', title: LF_RACES[r - 1] + ': ' + lfNames(slot)[r - 1], src: LF_IMG + cand[0] });
            im._cand = cand; im._n = 0;
            im.addEventListener('error', function () {
                this._n++;
                if (this._n < this._cand.length) this.src = LF_IMG + this._cand[this._n]; else this.style.visibility = 'hidden';
            });
            wrap.appendChild(im);
        }
        return wrap;
    }
    function lfCapRow(o, key) {
        var slot = Number(key.replace('MaxTechs', ''));
        return withHelp(row('Slot ' + slot, lfNames(slot).join(' / '), el('span', { class: 'cfg-lfrow' }, [lfImgs(slot), num(o, key, [key], { min: 0 })])), 'Brain.LifeformAutoResearch.MaxTechs' + slot);
    }
    function lfPriorityPreview(o) {
        var box = el('div', { class: 'cfg-lfprio' });
        (o.PriorityTechs || []).forEach(function (slot, i) {
            box.appendChild(el('span', { class: 'cfg-lfchip', title: lfNames(slot).join(' / ') }, [(i + 1) + '. ', lfImgs(slot), ' ' + slot]));
        });
        return (o.PriorityTechs || []).length ? withHelp(row('Order', 'Researched first, top to bottom', box), 'Brain.LifeformAutoResearch.PriorityTechs') : null;
    }
    function lfCapGrid(o) {
        var keys = numKeys(o, /^MaxTechs\d+$/);
        if (!Array.isArray(o.PriorityTechs)) o.PriorityTechs = [];
        var labels = [];
        function refresh() {
            labels.forEach(function (l) {
                var i = o.PriorityTechs.indexOf(l.slot);
                l.el.textContent = 'Prio'; l.el.parentNode.title = i >= 0 ? 'Priority ' + (i + 1) : 'Research this tech first';
            });
        }
        var grid = el('div', { class: 'cfg-lfgrid' });
        [1, 2, 3].forEach(function (tier) {
            var col = el('div', { class: 'cfg-lfcol' }, [el('b', { text: 'T' + tier })]);
            keys.filter(function (k) { return Math.floor(Number(k.replace('MaxTechs', '')) / 10) === tier; }).forEach(function (key) {
                var slot = Number(key.replace('MaxTechs', ''));
                var cb = el('input', { type: 'checkbox' });
                cb.checked = o.PriorityTechs.indexOf(slot) >= 0;
                var lab = el('span', { class: 'cfg-lfprio-l', text: 'Prio' });
                labels.push({ slot: slot, el: lab });
                cb.addEventListener('change', function () {
                    var i = o.PriorityTechs.indexOf(slot);
                    if (cb.checked && i < 0) o.PriorityTechs.push(slot);
                    if (!cb.checked && i >= 0) o.PriorityTechs.splice(i, 1);
                    refresh(); update();
                });
                col.appendChild(el('div', { class: 'cfg-lfcaprow', title: lfNames(slot).join(' / ') }, [
                    el('span', { class: 'cfg-lfslot', text: String(slot) }), lfImgs(slot), num(o, key, [key], { min: 0 }),
                    el('label', { class: 'cfg-lfprio-cb', title: 'Research this tech first' }, [cb, lab])]));
            });
            grid.appendChild(col);
        });
        refresh();
        return el('div', null, [el('div', { class: 'cfg-note', text: 'Level cap per slot (one tech per race in each slot, 0 = do not research). The tick marks a slot as priority: ticked slots are researched first, in the order they were ticked (hover the tick to see the position).' }), grid]);
    }
    var BRAIN_HELP = {
        Transports: 'Shared settings for every Brain worker that moves resources: where it collects from, which cargo ships it uses and how many slots it may take. Pathfinders are never used.',
        AutoMine: 'Builds mines, storage, energy and facilities up to the caps you set. 0 = do not build. The caps at the top are for the home world, "Colony" is for all other planets, single planets can get their own caps.',
        AutoResearch: 'Researches technologies up to the caps you set on the chosen planet. 0 = do not research. The priority switches decide what is researched first.',
        LifeformAutoMine: 'Builds lifeform buildings towards the target row of lifeform_targets.json, once the crystal mine of a planet is high enough.',
        LifeformAutoResearch: 'Researches lifeform techs from the priority list up to the level caps. It does not start new techs on its own.',
        AutoCargo: 'Builds cargo ships on your celestials, up to the amounts you set.',
        AutoDefence: 'Builds defence until the target counts are reached. 0 = do not build, shield domes are on/off.',
        AutoRepatriate: 'Sends resources from your planets to one target (for example the home moon) once enough has piled up.',
        BuyOfferOfTheDay: 'Buys the daily merchant offer.',
        KeepBoosters: 'Keeps resource boosters running: when none of a type is active on a planet, one from your inventory is activated. Nothing is bought with Dark Matter. Start with test mode on and read the log.',
        AutoFleetJumpGate: 'Jumps the fleet from the moon to a target moon through the jump gate, if enough fleet points would go.'
    };
    function boosterTiers(k, o, p) {
        var TIERS = ['bronze', 'silver', 'gold', 'platinum'], LABEL = { bronze: 'Bronze', silver: 'Silver', gold: 'Gold', platinum: 'Platinum' };
        if (Array.isArray(o.Tiers)) { var old = o.Tiers.map(function (t) { return String(t).toLowerCase(); }); o.Tiers = {}; TIERS.forEach(function (t) { o.Tiers[t] = old.indexOf(t) >= 0; }); }
        if (!o.Tiers || typeof o.Tiers !== 'object') { o.Tiers = {}; TIERS.forEach(function (t) { o.Tiers[t] = t !== 'platinum'; }); }
        if (typeof o.Reserve !== 'object' || o.Reserve === null) { var r0 = typeof o.Reserve === 'number' ? o.Reserve : 10; o.Reserve = {}; TIERS.forEach(function (t) { o.Reserve[t] = r0; }); }
        TIERS.forEach(function (t) { if (o.Tiers[t] === undefined) o.Tiers[t] = false; if (o.Reserve[t] === undefined) o.Reserve[t] = 10; });
        var spans = {};
        var rows = TIERS.map(function (t) {
            var sp = el('span', { class: 'cfg-unit', text: '/ … available' });
            spans[t] = sp;
            var ctl = el('div', { class: 'cfg-inline' }, [
                toggle(o.Tiers, t, p.concat('Tiers', t)),
                el('span', { class: 'cfg-unit', text: ' keep ' }),
                num(o.Reserve, t, p.concat('Reserve', t), { min: 0 }),
                sp
            ]);
            return row(LABEL[t], t === 'bronze' ? 'Switch = may be used. The number is kept in the inventory.' : null, ctl);
        });
        k.push(inner('brain:Boost:tiers', 'Booster tiers: use + reserve (Bronze is used first)', rows));
        fetch(main.dataset.boosterstock + '?instance=' + encodeURIComponent(instance), { cache: 'no-store' })
            .then(function (r) { return r.json(); })
            .then(function (r) {
                TIERS.forEach(function (t) {
                    var s = r.success && r.data && r.data.stock ? r.data.stock[t] : null;
                    if (!s) { spans[t].textContent = '/ no data yet'; return; }
                    spans[t].textContent = '/ ' + ['Metal', 'Crystal', 'Deuterium', 'Energy'].map(function (n) { return n.charAt(0) + ' ' + (s[n] || 0); }).join(' · ') + ' available';
                });
            })
            .catch(function () { TIERS.forEach(function (t) { spans[t].textContent = ''; }); });
    }
    function brainSub(body, name, title, build, rootObj, prefix) {
        var o = (rootObj || state)[name];
        if (!o || typeof o !== 'object' || Array.isArray(o)) return;
        var p = (prefix || []).concat(name);
        var on = o.Active !== false;
        var kids = [fld(o, 'Active', p, title + ' active', 'Switch', 'toggle'), el('div', { class: 'cfg-note', text: BRAIN_HELP[name] || '' })];
        if (!on) kids.push(el('div', { class: 'cfg-note', text: title + ' is switched off - only help is shown. Your settings are kept.' }));
        else build(kids, o, p);
        var key = 'brain:' + name;
        var det = el('details', { class: 'cfg-sec cfg-more' }, [el('summary', { text: title + (has(o, 'Active') ? (on ? '   ● on' : '   ○ off') : '') })].concat(kids.filter(Boolean)));
        if (openSecs[key]) det.open = true;
        det.addEventListener('toggle', function () { openSecs[key] = det.open; });
        body.appendChild(det);
    }
    function buildKeepBoosters(k, o, p) {
        k.push(fld(o, 'DryRun', p, 'Test mode (nothing activated)', 'Only the log shows what would be activated', 'toggle'));
        if (o.Types && typeof o.Types === 'object') {
            var tp = p.concat('Types');
            k.push(inner('brain:Boost:types', 'Booster types to keep running', autoRows(o.Types, tp, ['Metal', 'Crystal', 'Deuterium', 'Energy'])));
        }
        k.push(fld(o, 'LogItems', p, 'Log found items', 'Writes the active items of each planet and the inventory to the log, at most every 12 hours', 'toggle'));
        boosterTiers(k, o, p);
        if (o.MinMineLevel && typeof o.MinMineLevel === 'object') {
            k.push(inner('brain:Boost:minlevel', 'Minimum mine level (no booster below it, 0 = always)', autoRows(o.MinMineLevel, p.concat('MinMineLevel'), ['Metal', 'Crystal', 'Deuterium'])));
        }
        k.push(coordList(o, 'Exclude', 'Excluded planets', 'Never touched', '+ Add exclusion', p));
        intervalRows(o, p).forEach(function (r) { k.push(r); });
        restRows(o, p, ['Active', 'DryRun', 'Types', 'Reserve', 'MinMineLevel', 'LogItems', 'Tiers', 'Exclude', 'CheckIntervalMin', 'CheckIntervalMax']).forEach(function (r) { k.push(r); });
    }
    function renderBrain(body) {
        var d = state;
        body.appendChild(sec('Brain', [
            fld(d, 'Active', [], 'Brain active', 'Master switch for all Brain workers below', 'toggle'),
            el('div', { class: 'cfg-note', text: 'Each worker below has its own switch. Open a worker to see its options; a switched-off worker only shows its help.' })
        ].filter(Boolean)));

        brainSub(body, 'Transports', 'Transports (shared)', function (k, o, p) {
            var mo = o.MultipleOrigins;
            k.push(gname('Origin'));
            coordRows(o.Origin, p.concat('Origin')).forEach(function (r) { k.push(r); });
            k.push(fld(o, 'CargoType', p, 'Cargo ship', null, 'select', { options: ['SmallCargo', 'LargeCargo'] }));
            k.push(fld(o, 'CargoFallback', p, 'Fallback cargo ships', 'Comma separated', 'strlist'));
            k.push(fld(o, 'MixFleet', p, 'Mix small and large cargo', 'Last step if one type is not enough; flies at the speed of the slowest', 'toggle'));
            k.push(fld(o, 'UseTransportChain', p, 'Plan transports as a chain', null, 'toggle'));
            k.push(fld(o, 'CompactSlotPlan', p, 'Compact slot plan', 'Replan with fewer shipments if slots are short', 'toggle'));
            k.push(fld(o, 'MaxSlots', p, 'Max slots', 'Slots Brain transports may use at the same time', 'num', { min: 0 }));
            if (!has(o, 'DeutToLeaveOnPlanets')) o.DeutToLeaveOnPlanets = 0;
            k.push(fld(o, 'DeutToLeaveOnMoons', p, 'Deuterium left on moons', 'Transports out of a moon only go if payload + this reserve + the flight fuel fit', 'num', { min: 0 }));
            k.push(fld(o, 'DeutToLeaveOnPlanets', p, 'Deuterium left on planets', 'Same reserve for transports out of a planet (0 = keep nothing)', 'num', { min: 0 }));
            k.push(fld(o, 'RoundResources', p, 'Round the amount sent', null, 'toggle'));
            k.push(fld(o, 'SendToTheMoonIfPossible', p, 'Deliver to the moon if there is one', null, 'toggle'));
            k.push(fld(o, 'CheckMoonOrPlanetFirst', p, 'Check moon or planet first', null, 'toggle'));
            k.push(fld(o, 'DoMultipleTransportIsNotEnoughShipButSamePosition', p, 'Several transports from one position', 'If one ship type is not enough', 'toggle'));
            if (o.KeepOnOrigin && typeof o.KeepOnOrigin === 'object')
                k.push(inner('brain:Transports:keep', 'Ships kept on the origin', [genericObject(o.KeepOnOrigin, Object.keys(o.KeepOnOrigin), p.concat('KeepOnOrigin'))]));
            if (mo && typeof mo === 'object') {
                var mp = p.concat('MultipleOrigins');
                k.push(inner('brain:Transports:mo', 'Collect from several celestials', [
                    fld(mo, 'Active', mp, 'Active', null, 'toggle'),
                    fld(mo, 'OnlyFromMoons', mp, 'Only from moons', null, 'toggle'),
                    fld(mo, 'MinimumResourcesToSend', mp, 'Min resources to send', null, 'num', { min: 0 }),
                    fld(mo, 'PriorityToProximityOverQuantity', mp, 'Prefer near over large', null, 'toggle'),
                    Array.isArray(mo.Exclude) ? withHelp(row('Excluded', 'Never collected from', listTable(mo.Exclude, COORD_COLS, newCoord, '+ Add exclusion', p.concat('MultipleOrigins', 'Exclude'))), 'Brain.Transports.MultipleOrigins.Exclude') : null
                ]));
            }
            restRows(o, p, ['Active', 'Origin', 'CargoType', 'CargoFallback', 'MixFleet', 'UseTransportChain', 'CompactSlotPlan', 'MaxSlots', 'DeutToLeaveOnMoons', 'DeutToLeaveOnPlanets', 'RoundResources', 'SendToTheMoonIfPossible', 'CheckMoonOrPlanetFirst', 'DoMultipleTransportIsNotEnoughShipButSamePosition', 'KeepOnOrigin', 'MultipleOrigins']).forEach(function (r) { k.push(r); });
        });

        brainSub(body, 'AutoMine', 'AutoMine', function (k, o, p) {
            k.push(transportRow(o, p));
            if (o.HomeWorld) { k.push(gname('Home world')); coordRows(o.HomeWorld, p.concat('HomeWorld')).forEach(function (r) { k.push(r); }); }
            k.push(inner('brain:AutoMine:home', 'Caps for the home world', autoRows(o, p, numKeys(o, /^Max/))));
            if (o.Colony && typeof o.Colony === 'object')
                k.push(inner('brain:AutoMine:colony', 'Caps for all other planets (Colony)', autoRows(o.Colony, p.concat('Colony'), numKeys(o.Colony, /^Max/))));
            if (Array.isArray(o.ColonyOverrides)) {
                var ovKids = [el('div', { class: 'cfg-note', text: 'Single planets with their own caps. Matched by exact coordinate; only the fields you set replace the Colony caps.' })];
                o.ColonyOverrides.forEach(function (ov, idx) {
                    var op = p.concat('ColonyOverrides', idx);
                    var label = sens(el('span', { text: ov.Galaxy + ':' + ov.System + ':' + ov.Position + ' ' + (ov.Type || 'Planet') }));
                    var d2 = el('details', { class: 'cfg-inner' }, [el('summary', null, [label])].concat(coordRows(ov, op)).concat(autoRows(ov, op, numKeys(ov, /^Max/))).concat([
                        el('button', { type: 'button', class: 'cfg-btn sm', text: 'Remove this planet', onclick: function () { o.ColonyOverrides.splice(idx, 1); update(); render(); } })]));
                    ovKids.push(d2);
                });
                ovKids.push(el('button', { type: 'button', class: 'cfg-btn sm', text: '+ Add planet', onclick: function () { o.ColonyOverrides.push(newCoord()); openSecs['brain:AutoMine:ov'] = true; update(); render(); } }));
                k.push(inner('brain:AutoMine:ov', 'Planets with own caps (' + o.ColonyOverrides.length + ')', ovKids));
            }
            k.push(inner('brain:AutoMine:opt', 'Build options', autoRows(o, p, ['RandomOrder', 'OptimizeForStart', 'PrioritizeRobotsAndNanites', 'BuildDepositIfFull', 'BuildSolarSatellites', 'BuildCrawlers', 'FixedCrawlerTarget', 'DepositHours', 'MaxDaysOfInvestmentReturn', 'DeutToLeaveOnMoons'])));
            k.push(coordList(o, 'Exclude', 'Excluded celestials', 'Never touched', '+ Add exclusion', p));
            intervalRows(o, p).forEach(function (r) { k.push(r); });
            restRows(o, p, ['Active', 'Transports', 'HomeWorld', 'Colony', 'ColonyOverrides', 'RandomOrder', 'OptimizeForStart', 'PrioritizeRobotsAndNanites', 'BuildDepositIfFull', 'BuildSolarSatellites', 'SolarSatelliteBatch', 'EnergyReservePercent', 'BuildCrawlers', 'FixedCrawlerTarget', 'DepositHours', 'MaxDaysOfInvestmentReturn', 'DeutToLeaveOnMoons', 'Exclude', 'CheckIntervalMin', 'CheckIntervalMax'].concat(numKeys(o, /^Max/))).forEach(function (r) { k.push(r); });
        });

        brainSub(body, 'AutoResearch', 'AutoResearch', function (k, o, p) {
            k.push(transportRow(o, p));
            if (o.Target) { k.push(gname('Research planet')); coordRows(o.Target, p.concat('Target')).forEach(function (r) { k.push(r); }); }
            k.push(inner('brain:AutoResearch:caps', 'Research caps (0 = do not research)', autoRows(o, p, numKeys(o, /^Max/))));
            k.push(inner('brain:AutoResearch:prio', 'Priorities (researched first)', autoRows(o, p, boolKeys(o, /^Prioritize/))));
            k.push(fld(o, 'OptimizeForStart', p, 'Early-game order', null, 'toggle'));
            k.push(fld(o, 'EnsureExpoSlots', p, 'Make sure there are expedition slots', 'Researches Computer Technology / Astrophysics as needed', 'toggle'));
            k.push(fld(o, 'ForceResearchWhateverTheLabLevel', p, 'Research even with a low lab', null, 'toggle'));
            intervalRows(o, p).forEach(function (r) { k.push(r); });
            restRows(o, p, ['Active', 'Transports', 'Target', 'OptimizeForStart', 'EnsureExpoSlots', 'ForceResearchWhateverTheLabLevel', 'CheckIntervalMin', 'CheckIntervalMax'].concat(numKeys(o, /^Max/), boolKeys(o, /^Prioritize/))).forEach(function (r) { k.push(r); });
        });

        brainSub(body, 'LifeformAutoMine', 'LifeformAutoMine', function (k, o, p) {
            k.push(transportRow(o, p));
            k.push(fld(o, 'StartFromCrystalMineLvl', p, 'Start at crystal mine level', 'Lifeform buildings start once the crystal mine of a planet reaches this level', 'num', { min: 0 }));
            k.push(fld(o, 'TargetSlot', p, 'Target row', 'Row 1-18 of lifeform_targets.json; 0 = fallback caps only', 'num', { min: 0, max: 18 }));
            k.push(fld(o, 'MaxBasePopulationBuilding', p, 'Max population building', 'Extra limit for small accounts; the lower of table and limit counts. 0 = no extra limit', 'num', { min: 0 }));
            k.push(fld(o, 'MaxBaseFoodBuilding', p, 'Max food building', null, 'num', { min: 0 }));
            k.push(fld(o, 'MaxBaseTechBuilding', p, 'Max tech building', null, 'num', { min: 0 }));
            k.push(fld(o, 'PreventIfMoreExpensiveThanNextMine', p, 'Not if dearer than the next mine upgrade', null, 'toggle'));
            k.push(coordList(o, 'Exclude', 'Excluded celestials', 'Never touched', '+ Add exclusion', p));
            intervalRows(o, p).forEach(function (r) { k.push(r); });
            restRows(o, p, ['Active', 'Transports', 'StartFromCrystalMineLvl', 'TargetSlot', 'MaxBasePopulationBuilding', 'MaxBaseFoodBuilding', 'MaxBaseTechBuilding', 'PreventIfMoreExpensiveThanNextMine', 'Exclude', 'CheckIntervalMin', 'CheckIntervalMax']).forEach(function (r) { k.push(r); });
        });

        brainSub(body, 'LifeformAutoResearch', 'LifeformAutoResearch', function (k, o, p) {
            k.push(transportRow(o, p));
            k.push(fld(o, 'MaxResearchLevel', p, 'Highest level per tech', null, 'num', { min: 0 }));
            k.push(lfCapGrid(o));
            k.push(coordList(o, 'Exclude', 'Excluded celestials', 'Never touched', '+ Add exclusion', p));
            intervalRows(o, p).forEach(function (r) { k.push(r); });
            restRows(o, p, ['Active', 'Transports', 'PriorityTechs', 'MaxResearchLevel', 'Exclude', 'CheckIntervalMin', 'CheckIntervalMax'].concat(numKeys(o, /^MaxTechs\d+$/))).forEach(function (r) { k.push(r); });
        });

        brainSub(body, 'AutoCargo', 'AutoCargo', function (k, o, p) {
            k.push(fld(o, 'CargoType', p, 'Cargo ship to build', null, 'select', { options: ['SmallCargo', 'LargeCargo'] }));
            k.push(fld(o, 'MaxCargosToBuild', p, 'Target amount', null, 'num', { min: 0 }));
            k.push(fld(o, 'MaxCargosToKeep', p, 'Keep amount', null, 'num', { min: 0 }));
            k.push(fld(o, 'LimitToCapacity', p, 'Only as many as the stock needs', null, 'toggle'));
            k.push(fld(o, 'MaxProductionHours', p, 'Longest production time', '0 = no limit. One order per celestial never takes longer than this; the rest follows in the next run', 'num', { min: 0, unit: 'hours' }));
            k.push(fld(o, 'SupplyMoonFromPlanet', p, 'Moons get ships from the planet', null, 'toggle'));
            k.push(fld(o, 'ExcludeMoons', p, 'Skip moons', null, 'toggle'));
            k.push(fld(o, 'RandomOrder', p, 'Random order', null, 'toggle'));
            k.push(fld(o, 'SkipIfIncomingTransport', p, 'Skip if a transport is on its way', null, 'toggle'));
            k.push(coordList(o, 'Exclude', 'Excluded celestials', 'Never touched', '+ Add exclusion', p));
            intervalRows(o, p).forEach(function (r) { k.push(r); });
            restRows(o, p, ['Active', 'CargoType', 'MaxCargosToBuild', 'MaxCargosToKeep', 'LimitToCapacity', 'MaxProductionHours', 'SupplyMoonFromPlanet', 'ExcludeMoons', 'RandomOrder', 'SkipIfIncomingTransport', 'Exclude', 'CheckIntervalMin', 'CheckIntervalMax']).forEach(function (r) { k.push(r); });
        });

        brainSub(body, 'AutoDefence', 'AutoDefence', function (k, o, p) {
            k.push(fld(o, 'ExcludeMoons', p, 'Skip moons', null, 'toggle'));
            k.push(fld(o, 'RandomOrder', p, 'Random order', null, 'toggle'));
            k.push(fld(o, 'MaxProductionHours', p, 'Longest production time', '0 = no limit. One order per celestial never takes longer than this; the rest follows in the next run', 'num', { min: 0, unit: 'hours' }));
            if (o.DefenceToReach && typeof o.DefenceToReach === 'object') {
                var dp = p.concat('DefenceToReach');
                k.push(inner('brain:AutoDefence:t', 'Target per defence type (0 = do not build)', autoRows(o.DefenceToReach, dp, Object.keys(o.DefenceToReach))));
            }
            k.push(coordList(o, 'Exclude', 'Excluded celestials', 'Never touched', '+ Add exclusion', p));
            intervalRows(o, p).forEach(function (r) { k.push(r); });
            restRows(o, p, ['Active', 'ExcludeMoons', 'RandomOrder', 'MaxProductionHours', 'DefenceToReach', 'Exclude', 'CheckIntervalMin', 'CheckIntervalMax']).forEach(function (r) { k.push(r); });
        });

        brainSub(body, 'AutoRepatriate', 'AutoRepatriate', function (k, o, p) {
            k.push(coordList(o, 'Target', 'Target celestials', 'Where the resources go', '+ Add target', p));
            k.push(fld(o, 'TargetAssociateMoon', p, 'Deliver to the moon at the target', null, 'toggle'));
            k.push(fld(o, 'MinimumResources', p, 'Minimum to send', 'Only send if at least this much can go', 'num', { min: 0 }));
            if (o.LeaveDeut && typeof o.LeaveDeut === 'object') {
                var lp = p.concat('LeaveDeut');
                k.push(fld(o.LeaveDeut, 'DeutToLeave', lp, 'Deuterium left behind', null, 'num', { min: 0 }));
                k.push(fld(o.LeaveDeut, 'OnlyOnMoons', lp, '... only on moons', null, 'toggle'));
            }
            k.push(fld(o, 'CargoType', p, 'Cargo ship', null, 'select', { options: ['SmallCargo', 'LargeCargo'] }));
            k.push(fld(o, 'ExcludeMoons', p, 'Skip moons', null, 'toggle'));
            k.push(fld(o, 'RandomOrder', p, 'Random order', null, 'toggle'));
            k.push(fld(o, 'SkipIfIncomingTransport', p, 'Skip if a transport is on its way', null, 'toggle'));
            k.push(coordList(o, 'Exclude', 'Excluded celestials', 'Never touched', '+ Add exclusion', p));
            intervalRows(o, p).forEach(function (r) { k.push(r); });
            restRows(o, p, ['Active', 'Target', 'TargetAssociateMoon', 'MinimumResources', 'LeaveDeut', 'CargoType', 'ExcludeMoons', 'RandomOrder', 'SkipIfIncomingTransport', 'Reverse', 'Exclude', 'CheckIntervalMin', 'CheckIntervalMax']).forEach(function (r) { k.push(r); });
        });

        brainSub(body, 'BuyOfferOfTheDay', 'BuyOfferOfTheDay', function (k, o, p) {
            intervalRows(o, p).forEach(function (r) { k.push(r); });
            restRows(o, p, ['Active', 'CheckIntervalMin', 'CheckIntervalMax']).forEach(function (r) { k.push(r); });
        });

        brainSub(body, 'AutoFleetJumpGate', 'AutoFleetJumpGate', function (k, o, p) {
            if (Array.isArray(o.Target)) k.push(withHelp(row('Target moons', 'Where the fleet jumps to (jump gates exist on moons only; only the first entry is used)',
                listTable(o.Target, COORD_COLS.slice(0, 3), function () { return { Galaxy: 1, System: 1, Position: 1, Type: 'Moon' }; }, '+ Add target', ['Target'])), fullKey(p, 'Target')));
            k.push(fld(o, 'MinimumAmountOfShipPointsToTeleport', p, 'Min fleet points to jump', null, 'num', { min: 0 }));
            if (!Array.isArray(o.Exclude)) o.Exclude = [];
            k.push(withHelp(row('Excluded moons', 'These moons never jump', listTable(o.Exclude, COORD_COLS.slice(0, 3), function () { return { Galaxy: 1, System: 1, Position: 1, Type: 'Moon' }; }, '+ Add exclusion', ['Exclude'])), fullKey(p, 'Exclude')));
            if (o.MinimumAmountOfShipsToBeLeftOnMoon && typeof o.MinimumAmountOfShipsToBeLeftOnMoon === 'object') {
                var mp = p.concat('MinimumAmountOfShipsToBeLeftOnMoon'), M = o.MinimumAmountOfShipsToBeLeftOnMoon;
                k.push(inner('brain:Jump:left', 'Ships that stay on the moon', autoRows(M, mp, Object.keys(M))));
            }
            intervalRows(o, p).forEach(function (r) { k.push(r); });
            restRows(o, p, ['Active', 'Target', 'Exclude', 'MinimumAmountOfShipPointsToTeleport', 'MinimumAmountOfShipsToBeLeftOnMoon', 'CheckIntervalMin', 'CheckIntervalMax']).forEach(function (r) { k.push(r); });
        });

        var known = ['Active', 'Transports', 'AutoMine', 'AutoResearch', 'LifeformAutoMine', 'LifeformAutoResearch', 'AutoCargo', 'AutoDefence', 'AutoRepatriate', 'BuyOfferOfTheDay', 'KeepBoosters', 'AutoFleetJumpGate'];
        var rest = Object.keys(d).filter(function (k) { return known.indexOf(k) < 0; });
        if (rest.length) foldTo(body, 'More settings (' + rest.length + ')', [genericObject(d, rest, [])]);
    }

    function renderGeneralPage(body) {
        var d = state, P = [];
        body.appendChild(sec('Connection', [
            el('div', { class: 'cfg-note', text: 'Address of the local ogamed connector the bot talks to.' }),
            fld(d, 'Host', P, 'Host', null, 'text', { sens: true }),
            fld(d, 'Port', P, 'Port', null, 'text', { sens: true })
        ].filter(Boolean)));
        if (d.Proxy && typeof d.Proxy === 'object') {
            var px = d.Proxy, pp = ['Proxy'];
            body.appendChild(sec('Proxy', [
                fld(px, 'Enabled', pp, 'Use a proxy', null, 'toggle'),
                fld(px, 'Address', pp, 'Address', null, 'text', { sens: true }),
                fld(px, 'Type', pp, 'Type', null, 'select', { options: ['socks5', 'socks4', 'http', 'https'] }),
                fld(px, 'LoginOnly', pp, 'Only for the login', 'On: game requests do not go through the proxy', 'toggle'),
                el('div', { class: 'cfg-note', text: 'Proxy user name and password and the captcha key are neither shown nor editable in the web UI. Set them directly in the config file (secrets/).' })
            ].filter(Boolean)));
        }
        body.appendChild(sec('Fleet slots', [
            fld(d, 'SlotsToLeaveFree', P, 'Slots to leave free', 'No worker may use these slots - they stay free for you', 'num', { min: 0 })
        ].filter(Boolean)));
        if (d.SlotPriorityLevel && typeof d.SlotPriorityLevel === 'object') {
            var sp = d.SlotPriorityLevel;
            body.appendChild(sec('Slot priority', [
                el('div', { class: 'cfg-note', text: 'Who gets a free slot first when several workers compete. Lower number = served first.' })
            ].concat(Object.keys(sp).map(function (k) { return fld(sp, k, ['SlotPriorityLevel'], k, null, 'num', { min: 0 }); }))));
        }
        var rest = Object.keys(d).filter(function (k) { return ['Host', 'Port', 'Proxy', 'SlotsToLeaveFree', 'SlotPriorityLevel', 'CaptchaAPIKey', 'BasicAuth', 'LoginPause', 'BotPause'].indexOf(k) < 0; });
        if (rest.length) foldTo(body, 'More settings (' + rest.length + ')', [genericObject(d, rest, [])]);
    }

    function renderAutoHarvest(body) {
        var d = state, P = [];
        body.appendChild(sec('AutoHarvest', [
            fld(d, 'Active', P, 'AutoHarvest active', 'Master switch', 'toggle'),
            fld(d, 'MaxSlots', P, 'Max slots', null, 'num', { min: 0 }),
            el('div', { class: 'cfg-note', text: 'Without an Origin of its own the moon at the Defender "Home" coordinates is used as the start.' })
        ].filter(Boolean)));
        body.appendChild(sec('Which debris fields', [
            fld(d, 'HarvestOwnDF', P, 'Own debris fields', null, 'toggle'),
            fld(d, 'MinimumResourcesOwnDF', P, 'Minimum content (own)', 'Smaller fields are not worth a trip', 'num', { min: 0 }),
            fld(d, 'HarvestForeignDF', P, 'Foreign debris fields', null, 'toggle'),
            fld(d, 'MinimumResourcesForeignDF', P, 'Minimum content (foreign)', null, 'num', { min: 0 }),
            fld(d, 'HarvestDeepSpace', P, 'Deep space', null, 'toggle'),
            fld(d, 'MinimumResourcesDeepSpace', P, 'Minimum content (deep space)', null, 'num', { min: 0 }),
            fld(d, 'DeepSpaceRange', P, 'Deep space range', 'Systems around the origin that are searched', 'num', { min: 0 })
        ].filter(Boolean)));
        foldTo(body, 'Timing', intervalRows(d, P));
        var rest = Object.keys(d).filter(function (k) { return ['Active', 'MaxSlots', 'HarvestOwnDF', 'MinimumResourcesOwnDF', 'HarvestForeignDF', 'MinimumResourcesForeignDF', 'HarvestDeepSpace', 'MinimumResourcesDeepSpace', 'DeepSpaceRange', 'CheckIntervalMin', 'CheckIntervalMax'].indexOf(k) < 0; });
        if (rest.length) foldTo(body, 'More settings (' + rest.length + ')', [genericObject(d, rest, [])]);
    }

    function renderAutoColonize(body) {
        var d = state, P = [];
        body.appendChild(sec('AutoColonize', [
            fld(d, 'Active', P, 'AutoColonize active', 'Master switch', 'toggle'),
            fld(d, 'SlotsToLeaveFree', P, 'Slots to leave free', 'Kept free for this worker\'s rounds', 'num', { min: 0 }),
            fld(d, 'RandomPosition', P, 'Random position in the targets', null, 'toggle')
        ].filter(Boolean)));
        if (d.Origin) body.appendChild(sec('Origin', [el('div', { class: 'cfg-note', text: 'Where the colony ships start.' })].concat(coordRows(d.Origin, ['Origin']))));
        if (Array.isArray(d.Targets)) body.appendChild(sec('Target ranges', [
            el('div', { class: 'cfg-note', text: 'Ranges to colonise. Max planets = stop after this many colonies in that range.' }),
            listTable(d.Targets, [
                { key: 'Galaxy', label: 'Galaxy', min: 1, sens: true }, { key: 'StartSystem', label: 'From system', min: 1, sens: true }, { key: 'EndSystem', label: 'To system', min: 1, sens: true },
                { key: 'StartPosition', label: 'From pos.', min: 1, sens: true }, { key: 'EndPosition', label: 'To pos.', min: 1, sens: true }, { key: 'MaxPlanets', label: 'Max planets', min: 0 }],
                function () { return { Galaxy: 1, StartSystem: 1, EndSystem: 499, StartPosition: 8, EndPosition: 8, MaxPlanets: 1 }; }, '+ Add range', ['Targets'])
        ]));
        if (d.Abandon && typeof d.Abandon === 'object') {
            var ab = d.Abandon, ap = ['Abandon'];
            foldTo(body, 'Abandon bad planets', [
                el('div', { class: 'cfg-note', text: 'Abandons new planets that are too small or have the wrong temperature. The home planet is never abandoned.' }),
                fld(ab, 'Active', ap, 'Abandon bad planets', null, 'toggle'),
                fld(ab, 'MinFields', ap, 'Min fields', null, 'num', { min: 0 }),
                fld(ab, 'MinTemperatureAcceptable', ap, 'Min temperature', null, 'num', { unit: '°C' }),
                fld(ab, 'MaxTemperatureAcceptable', ap, 'Max temperature', null, 'num', { unit: '°C' })
            ]);
        }
        if (d.IntensiveResearch && typeof d.IntensiveResearch === 'object') {
            var ir = d.IntensiveResearch, ip = ['IntensiveResearch'];
            foldTo(body, 'Intensive mode (bursts)', [
                el('div', { class: 'cfg-note', text: 'Colonises in bursts with several ships at once.' }),
                fld(ir, 'Active', ip, 'Intensive mode', null, 'toggle'),
                fld(ir, 'MaxSlots', ip, 'Max slots', null, 'num', { min: 0 }),
                fld(ir, 'MinWaitNextFleet', ip, 'Wait between fleets, min', null, 'num', { min: 0, unit: 'seconds' }),
                fld(ir, 'MaxWaitNextFleet', ip, 'Wait between fleets, max', null, 'num', { min: 0, unit: 'seconds' })
            ]);
        }
        foldTo(body, 'Timing', intervalRows(d, P));
        var rest = Object.keys(d).filter(function (k) { return ['Active', 'SlotsToLeaveFree', 'RandomPosition', 'Origin', 'Targets', 'Abandon', 'IntensiveResearch', 'CheckIntervalMin', 'CheckIntervalMax'].indexOf(k) < 0; });
        if (rest.length) foldTo(body, 'More settings (' + rest.length + ')', [genericObject(d, rest, [])]);
    }

    function renderAutoDiscovery(body) {
        var d = state, P = [];
        if (!has(d, 'StopAtArtifacts')) d.StopAtArtifacts = 0;
        body.appendChild(sec('AutoDiscovery', [
            fld(d, 'Active', P, 'AutoDiscovery active', 'Master switch', 'toggle'),
            fld(d, 'MaxSlots', P, 'Max slots', null, 'num', { min: 0 }),
            fld(d, 'StopAtArtifacts', P, 'Stop at artifacts', 'No more discoveries from this many collected artifacts (0 = no limit)', 'num', { min: 0 })
        ].filter(Boolean)));
        if (Array.isArray(d.Origin)) body.appendChild(sec('Origins', [
            el('div', { class: 'cfg-note', text: 'Celestials the discovery missions start from.' }),
            listTable(d.Origin, COORD_COLS, newCoord, '+ Add origin', ['Origin'])
        ]));
        foldTo(body, 'Timing', intervalRows(d, P));
        var rest = Object.keys(d).filter(function (k) { return ['Active', 'MaxSlots', 'StopAtArtifacts', 'Origin', 'CheckIntervalMin', 'CheckIntervalMax'].indexOf(k) < 0; });
        if (rest.length) foldTo(body, 'More settings (' + rest.length + ')', [genericObject(d, rest, [])]);
    }

    function renderFastFarm(body) {
        var d = state, P = [];
        body.appendChild(sec('FastFarm', [
            el('div', { class: 'cfg-note', text: 'Resource farming mode with one goal: collect as much deuterium as possible. It scans the galaxy for inactive players, spies them with a fixed number of probes and raids the best ones with cargo ships. While it is on, all other workers except the Defender stay off.' }),
            fld(d, 'Active', P, 'FastFarm active', 'Master switch', 'toggle'),
            fld(d, 'OnlyWorker', P, 'Switch off all other workers', 'Defender and sleep mode always stay on', 'toggle'),
            fld(d, 'KeepExpeditions', P, 'Keep expeditions running', 'Only used when the other workers are switched off', 'toggle')
        ].filter(Boolean)));
        if (Array.isArray(d.Origin)) body.appendChild(sec('Origins', [
            el('div', { class: 'cfg-note', text: 'Celestials the probes and cargo ships start from. The nearest origin with enough ships is used for every target.' }),
            listTable(d.Origin, COORD_COLS, newCoord, '+ Add origin', ['Origin'])
        ]));
        if (Array.isArray(d.ScanRange)) body.appendChild(sec('Scan area', [
            el('div', { class: 'cfg-note', text: 'Systems that are scanned for inactive players, nearest to your origins first. Leave empty to scan 100 systems around every origin.' }),
            listTable(d.ScanRange, [{ key: 'Galaxy', label: 'Galaxy', min: 1, sens: true }, { key: 'StartSystem', label: 'From system', min: 1, sens: true }, { key: 'EndSystem', label: 'To system', min: 1, sens: true }],
                function () { return { Galaxy: 1, StartSystem: 1, EndSystem: 499 }; }, '+ Add range', ['ScanRange'])
        ]));
        body.appendChild(sec('Sweep', [
            el('div', { class: 'cfg-note', text: 'FastFarm works in rounds: scan one galaxy, spy every target, sort by loot, then check the best ones again with fresh probes right before the attack. Targets whose loot shrank by the set share are dropped.' }),
            fld(d, 'ShrinkDiscardPercent', P, 'Drop target when loot shrank by', null, 'num', { min: 0, max: 100, unit: '%' })
        ].filter(Boolean)));
        body.appendChild(sec('Ships and slots', [
            fld(d, 'CargoType', P, 'Cargo ships', 'Both = small cargos first, large cargos for the rest', 'select', { options: ['SmallCargo', 'LargeCargo', 'Both'] }),
            fld(d, 'ProbesPerTarget', P, 'Probes per target', null, 'num', { min: 1 }),
            fld(d, 'SlotsToLeaveFree', P, 'Slots to leave free', 'Kept free for the Defender', 'num', { min: 0 }),
            fld(d, 'FleetSpeedPercent', P, 'Fleet speed', null, 'num', { min: 10, max: 100, step: 10, unit: '%' }),
            fld(d, 'CargoSurplusPercent', P, 'Extra cargo space', 'Added on top of the expected loot', 'num', { min: 0, unit: '%' }),
            fld(d, 'MinFillPercent', P, 'Minimum fill', 'Skip a target when the available ships carry less than this share of its loot', 'num', { min: 1, max: 100, unit: '%' })
        ].filter(Boolean)));
        body.appendChild(sec('Target choice', [
            el('div', { class: 'cfg-note', text: 'Targets are ranked by loot value = metal x weight + crystal x weight + deuterium x weight. The best ones are attacked first.' }),
            fld(d, 'WeightMetal', P, 'Weight metal', null, 'num', { min: 0, step: 'any' }),
            fld(d, 'WeightCrystal', P, 'Weight crystal', null, 'num', { min: 0, step: 'any' }),
            fld(d, 'WeightDeuterium', P, 'Weight deuterium', null, 'num', { min: 0, step: 'any' }),
            fld(d, 'MinLoot', P, 'Minimum loot', 'Metal + crystal + deuterium that can be taken', 'num', { min: 0 }),
            fld(d, 'MinDeuterium', P, 'Minimum deuterium', 'Deuterium that can be taken', 'num', { min: 0 }),
            fld(d, 'TopTargets', P, 'Top targets per round', null, 'num', { min: 1 })
        ].filter(Boolean)));
        foldTo(body, 'Reports and timing', [
            fld(d, 'DeleteReports', P, 'Delete reports after reading', 'Keeps the inbox empty so it does not fill up', 'toggle'),
            fld(d, 'ReportMaxAgeMinutes', P, 'Max report age', 'Older reports are not attacked', 'num', { min: 1, unit: 'minutes' }),
            fld(d, 'MaxSystemsPerScanStep', P, 'Systems per scan step', null, 'num', { min: 1 })
        ].filter(Boolean));
        var known = ['Active', 'OnlyWorker', 'KeepExpeditions', 'Origin', 'ScanRange', 'ShrinkDiscardPercent', 'CargoType', 'ProbesPerTarget', 'SlotsToLeaveFree', 'FleetSpeedPercent', 'CargoSurplusPercent', 'MinFillPercent', 'WeightMetal', 'WeightCrystal', 'WeightDeuterium', 'MinLoot', 'MinDeuterium', 'TopTargets', 'DeleteReports', 'ReportMaxAgeMinutes', 'MaxSystemsPerScanStep'];
        var rest = Object.keys(d).filter(function (k) { return known.indexOf(k) < 0; });
        if (rest.length) foldTo(body, 'More settings (' + rest.length + ')', [genericObject(d, rest, [])]);
    }

    function renderDebrisSave(body) {
        var d = state;
        body.appendChild(sec('DebrisSave', [
            el('div', { class: 'cfg-note', text: 'Used by the Telegram command /debrissave <duration>: each listed celestial sends its fleet to the nearest debris field and recalls it so it is back after the duration.' }),
            Array.isArray(d.Celestials) ? listTable(d.Celestials, COORD_COLS, newCoord, '+ Add celestial', ['Celestials']) : null
        ].filter(Boolean)));
        var rest = Object.keys(d).filter(function (k) { return k !== 'Celestials'; });
        if (rest.length) foldTo(body, 'More settings (' + rest.length + ')', [genericObject(d, rest, [])]);
    }

    var BLOCK_HELP = {
        Expeditions: ['Sends expeditions automatically from your origin celestials and refills the slots as the fleets return.', [
            ['Fleet', 'Automatic: as many cargo ships as needed to carry the largest possible find, plus 1 probe and the strongest combat ship. Or a fixed fleet per expedition.'],
            ['Slots', 'With several origins the expedition slots are shared round-robin (13 slots: 7+6, with 3 origins 5+4+4).'],
            ['Split systems', 'Spreads the targets over systems in a range.'],
            ['Sleep wave', 'One extra wave right before the bot falls asleep, timed to return around wake-up.'],
            ['Lifeform panel', 'Shows which lifeform bonuses currently raise finds and cargo.']]],
        AutoFarm: ['Scans systems for inactive players, spies them and attacks the profitable ones.', [
            ['Scan area', 'Which systems are scanned, in which order and how many per cycle.'],
            ['Targets', 'Minimum loot, defence limits, flight time and loot/fuel ratio decide what is attacked.'],
            ['Fleet', 'Cargo ships and optional escort; slot budget for AutoFarm.'],
            ['Dashboard', 'The AutoFarm page shows targets, attacks, blacklist, maintenance and the losses and profit list.']]],
        Defender: ['Watches for incoming attacks and reacts to them.', [
            ['Alerts', 'Telegram and Discord messages, optional alarm level.'],
            ['Fleetsave', 'Saves the fleet of the attacked celestial shortly before impact (safety margin).'],
            ['Filters', 'Ignore spy probes, weak attackers, attacks where there is nothing worth saving; whitelist.'],
            ['Counter-measures', 'Spy the attacker or send a message.'],
            ['Activity', 'Generates normal-looking page visits during the defender checks.']]],
        Brain: ['Automation of building, research and transports. Each sub-worker has its own switch.', [
            ['Transports', 'Shared settings for moving resources between your celestials.'],
            ['AutoMine / AutoResearch', 'Builds mines and facilities and researches technologies up to your caps.'],
            ['Lifeform workers', 'Build lifeform buildings and research lifeform techs from the target table.'],
            ['AutoCargo / AutoDefence', 'Build cargo ships and defence up to target amounts.'],
            ['AutoRepatriate', 'Collects resources from your planets on one target celestial.'],
            ['Offer / JumpGate', 'Buys the daily merchant offer; jumps the fleet through the jump gate.']]],
        AutoHarvest: ['Sends recyclers to debris fields that are worth the trip: your own, foreign ones and deep space.', []],
        AutoColonize: ['Colonises planets in the configured ranges and can abandon colonies that are too small or the wrong temperature.', []],
        AutoDiscovery: ['Sends discovery missions from the origin celestials.', []],
        FastFarm: ['Resource farming mode: scans for inactive players, spies them with a fixed number of probes and raids the best ones with cargo ships, aiming at deuterium. All other workers except the Defender stay off while it runs.', [
            ['Scan and spy', 'The background scan keeps a list of inactive players; every free slot sends one probe fleet.'],
            ['Attack', 'Targets with a fresh report are ranked by weighted loot and raided with small and/or large cargos.']]],
    };
    function helpCard(name, extra) {
        var h = BLOCK_HELP[name] || ['This function has no description yet.', []];
        var kids = [el('div', { class: 'cfg-note', text: h[0] })];
        if (h[1].length) {
            var ul = el('ul', { class: 'cfg-helplist' });
            h[1].forEach(function (x) { ul.appendChild(el('li', null, [el('b', { text: x[0] + ': ' }), x[1]])); });
            kids.push(ul);
        }
        return sec(extra || 'What it does', kids);
    }
    function renderOff(body) {
        body.appendChild(sec('Status', [
            row('Active', 'Switch it on to show and edit the options', toggle(state, 'Active', ['Active'])),
            el('div', { class: 'cfg-note', text: block + ' is switched off - only help is shown. Your settings are kept.' })
        ]));
        body.appendChild(helpCard(block));
    }

    function simpleDiff() {
        var a = shown(original).split('\n'), b = shown(state).split('\n');
        var box = el('div', { class: 'cfg-diff' }), sa = {}, sb = {};
        a.forEach(function (l) { sa[l] = (sa[l] || 0) + 1; }); b.forEach(function (l) { sb[l] = (sb[l] || 0) + 1; });
        var any = false;
        a.forEach(function (l) { if (!sb[l]) { box.appendChild(el('div', { class: 'del', text: '- ' + l })); any = true; } });
        b.forEach(function (l) { if (!sa[l]) { box.appendChild(el('div', { class: 'add', text: '+ ' + l })); any = true; } });
        if (!any) box.appendChild(el('div', { text: 'No changes.' }));
        return box;
    }

    var saveBtn, dirtyLbl;
    function syncDraft() {
        if (!state || !original) return;
        var dr = readDrafts();
        (COMBINED || [block]).forEach(function (n) {
            var cur = COMBINED ? state[n] : state, orig = COMBINED ? original[n] : original;
            if (cur !== undefined && JSON.stringify(cur) !== JSON.stringify(orig)) dr[n] = cur; else delete dr[n];
        });
        writeDrafts(dr); renderDraftBar();
    }
    function update() { if (saveBtn) { saveBtn.disabled = !dirty(); dirtyLbl.textContent = dirty() ? '● unsaved changes' : ''; } syncDraft(); }

    function maskLabel() {
        var cb = el('input', { type: 'checkbox' });
        cb.checked = mask;
        cb.addEventListener('change', function () {
            mask = cb.checked;
            if (window.tbotHideData) window.tbotHideData.set(mask);
            if (view !== 'form') render(); else applyMask();
        });
        return el('label', { class: 'cfg-masklbl' }, [cb, ' Hide data']);
    }

    function render() {
        root.innerHTML = '';
        sensNodes = [];
        var tabs = el('div', { class: 'cfg-tabs' });
        ['form', 'json', 'diff'].forEach(function (t) {
            var b = el('button', { type: 'button', text: t === 'form' ? 'Form' : t === 'json' ? 'JSON' : 'Diff', class: view === t ? 'on' : '' });
            b.addEventListener('click', function () {
                if (view === 'json' && !mask) { try { var parsed1 = JSON.parse(ta.value); restoreSecrets(parsed1, state); state = parsed1; } catch (e) { alert('Invalid JSON: ' + e.message); return; } }
                view = t; render();
            });
            tabs.appendChild(b);
        });
        root.appendChild(el('div', { class: 'cfg-head' }, [
            el('h1', { text: COMBINED ? COMBINED_TITLE : block }), sens(el('span', { class: 'sub grow', text: file })), helpLabel(), maskLabel(), tabs
        ]));
        if (addedDefaults.length) root.appendChild(el('div', { class: 'cfg-note' }, [
            el('b', { text: 'New options added with default values (saved with the next Save):' }), document.createTextNode(' '),
            el('span', { text: addedDefaults.map(function (p) { return p.split('.').slice(1).join('.'); }).join(', ') })
        ]));
        var body = el('div', { class: 'cfg-body' }), ta;
        if (view === 'form') {
            if (!COMBINED && state && typeof state.Active === 'boolean' && !state.Active) renderOff(body); else if (block === 'Expeditions') renderExpeditions(body); else if (block === 'AutoFarm') renderAutoFarm(body); else if (block === 'Defender') renderDefender(body); else if (block === 'Brain') renderBrain(body); else if (block === 'General') renderGeneralPage(body); else if (block === 'AutoHarvest') renderAutoHarvest(body); else if (block === 'AutoColonize') renderAutoColonize(body); else if (block === 'AutoDiscovery') renderAutoDiscovery(body); else if (block === 'FastFarm') renderFastFarm(body); else if (block === 'DebrisSave') renderDebrisSave(body); else if (COMBINED) { if (block === 'Messenger') renderMessengerPage(body); else if (block === 'Testing') renderTestingPage(body); else renderSleepPage(body); } else renderGeneric(body);
        } else if (view === 'json') {
            ta = el('textarea', { class: 'cfg-json', spellcheck: 'false' }); ta.value = shown(state); if (mask) { ta.readOnly = true; ta.title = 'Hide data is on - switch it off to edit'; } body.appendChild(ta);
        } else body.appendChild(simpleDiff());
        root.appendChild(body);

        saveBtn = el('button', { type: 'button', class: 'cfg-btn p', text: 'Save', onclick: save });
        dirtyLbl = el('span', { class: 'dirty' });
        root.appendChild(el('div', { class: 'cfg-foot' }, [
            dirtyLbl, el('span', { class: 'grow cfg-note', text: 'Changes are kept while you switch pages - "Save all" (banner at the top) writes every changed block at once. Backup (.bak) is created.' }),
            el('button', { type: 'button', class: 'cfg-btn', text: 'Discard', onclick: function () { (COMBINED || [block]).forEach(clearPendingActive); state = clone(original); render(); } }), saveBtn
        ]));
        applyMask();
        update();
    }

    function save() {
        saveBtn.disabled = true;
        var ta = root.querySelector('.cfg-json');
        if (ta && !ta.readOnly) { try { var parsed2 = JSON.parse(ta.value); restoreSecrets(parsed2, state); state = parsed2; } catch (e) { alert('Invalid JSON: ' + e.message); saveBtn.disabled = false; return; } }
        var post = postBlock;
        var saving = COMBINED
            ? COMBINED.filter(function (b) { return state[b] && JSON.stringify(state[b]) !== JSON.stringify(original[b]); }).reduce(function (p, b) {
                return p.then(function (res) { return res.success ? post(b, state[b]) : res; });
            }, Promise.resolve({ success: true }))
            : post(block, state);
        saving
            .then(function (r) {
                if (!r.success) { alert('Save failed: ' + r.error); saveBtn.disabled = false; return; }
                original = clone(state); originalText = JSON.stringify(original, null, 2); (COMBINED || [block]).forEach(function (n) { var sw0 = document.querySelector('#cfg-blocks .sw.click[data-block="' + n + '"]'); if (sw0) sw0.dataset.orig = ((COMBINED ? state[n] : state) || {}).Active ? '1' : '0'; clearPendingActive(n); }); render();
                dirtyLbl.textContent = '✓ saved'; dirtyLbl.style.color = 'var(--tb-ok)';
            })
            .catch(function (e) { alert('Save failed: ' + e); saveBtn.disabled = false; });
    }
})();
