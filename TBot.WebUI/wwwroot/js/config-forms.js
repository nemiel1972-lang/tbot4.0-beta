/* Config-Formulare: Expeditions (eigene Ansicht), andere freigeschaltete Bloecke ueber generischen Renderer. */
(function () {
    'use strict';
    var main = document.getElementById('cfg-main');
    var root = document.getElementById('cfg-root');
    if (!main || !root) { wireSearch(); return; }
    wireSearch();

    var instance = main.dataset.instance, block = main.dataset.block;
    var SENS_RE = /host|alliance|galaxy|^system$|position|coord|name|url|id$/i;
    var moreOpen = false, lfOpen = false;
    var mask = true;
    try { var ms0 = localStorage.getItem('config.mask'); if (ms0 !== null) mask = ms0 === '1'; } catch (e) { }
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
    // Kopie mit verdeckten Werten fuer JSON-/Diff-Ansicht (nur Anzeige, z. B. fuer Screenshots)
    function maskedCopy(o, key) {
        if (Array.isArray(o)) return o.map(function (x) { return maskedCopy(x, key); });
        if (o && typeof o === 'object') { var r = {}; Object.keys(o).forEach(function (k) { r[k] = maskedCopy(o[k], k); }); return r; }
        return key && SENS_RE.test(key) && o !== null && o !== '' ? '*****' : o;
    }
    function shown(o) { return JSON.stringify(mask ? maskedCopy(o) : o, null, 2); }
    function applyMask() { sensNodes.forEach(function (n) { setMasked(n, mask); }); }
    var original = null, state = null, originalText = '', view = 'form', file = '';

    var SHIPS = ['LightFighter', 'HeavyFighter', 'Cruiser', 'Battleship', 'Battlecruiser', 'Bomber', 'Destroyer', 'Reaper', 'Pathfinder', 'SmallCargo', 'LargeCargo', 'EspionageProbe'];
    // staerkstes Kampfschiff zuerst (entspricht der Auswahl im Worker)
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

    // ---------- Laden ----------
    fetch(main.dataset.get + '?instance=' + encodeURIComponent(instance) + '&block=' + encodeURIComponent(block), { cache: 'no-store' })
        .then(function (r) { return r.json(); })
        .then(function (r) {
            if (!r.success) { root.innerHTML = ''; root.appendChild(el('div', { class: 'cfg-error', text: r.error })); return; }
            original = JSON.parse(r.data); state = clone(original); originalText = JSON.stringify(original, null, 2); file = r.file;
            render();
        })
        .catch(function (e) { root.innerHTML = ''; root.appendChild(el('div', { class: 'cfg-error', text: String(e) })); });

    // ---------- Eingabe-Helfer ----------
    function changedClass(obj, key, orig) { return JSON.stringify(obj[key]) !== JSON.stringify(orig) ? ' cfg-changed' : ''; }
    function origAt(path) { var o = original; for (var i = 0; i < path.length; i++) { if (o == null) return undefined; o = o[path[i]]; } return o; }

    function toggle(obj, key, path) {
        var b = el('button', { type: 'button', class: 'cfg-toggle' + (obj[key] ? ' on' : '') });
        b.addEventListener('click', function () { obj[key] = !obj[key]; b.classList.toggle('on', !!obj[key]); update(); });
        return b;
    }
    function num(obj, key, path, opts) {
        opts = opts || {};
        var i = el('input', { type: 'number', value: obj[key] == null || (opts.empty0 && !obj[key]) ? '' : obj[key], min: opts.min, step: opts.step || 1, placeholder: opts.ph });
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

    // ---------- Expeditions ----------
    var handled = ['Active', 'Origin', 'ManualShips', 'PrimaryShip', 'MinPrimaryToSend', 'PrimaryToKeep', 'SleepWave'];

    function renderExpeditions(body) {
        var d = state;
        if (d.Active === undefined) d.Active = false;
        body.appendChild(sec('General', [
            row('Expeditions active', 'Master switch for the expedition worker', toggle(d, 'Active', ['Active']))
        ]));

        // Origins
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

        // Flotte: Manual / Auto
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
                fleetBox.appendChild(row('Primary ship', 'Cargo type that is calculated', select(d, 'PrimaryShip', ['PrimaryShip'], PRIMARY)));
                if (!d.PrimaryShip) d.PrimaryShip = 'LargeCargo';
                fleetBox.appendChild(row('Keep at home', 'PrimaryToKeep: ships of the primary type that stay home', num(d, 'PrimaryToKeep', ['PrimaryToKeep'], { min: 0 })));
                fleetBox.appendChild(row('Minimum to send', 'MinPrimaryToSend: no expedition if fewer primary ships are available (0 = off)', num(d, 'MinPrimaryToSend', ['MinPrimaryToSend'], { min: 0 })));
                fleetBox.appendChild(el('div', { class: 'cfg-note', text: 'Auto: cargo count from server top score and Lifeform bonus, plus 1 probe and the strongest combat ship (Pathfinder if none).' }));
            }
        }
        drawSeg(); drawFleet();
        body.appendChild(sec('Fleet (global)', [row('Mode', null, seg), fleetBox]));

        // SleepWave
        if (!d.SleepWave) d.SleepWave = { Active: false, ReturnAt: '', SpeedPercent: 50 };
        var sw = d.SleepWave;
        body.appendChild(sec('Sleep wave', [
            row('Active', 'Wave of expeditions that returns around wake-up', toggle(sw, 'Active', ['SleepWave', 'Active'])),
            row('Return at', 'HH:mm, WakeUp or WakeUp+15 (minutes); empty = fixed speed', text(sw, 'ReturnAt', ['SleepWave', 'ReturnAt'], function (v) { return TIME_RE.test(v.trim()); })),
            row('Speed (fallback)', 'Used when ReturnAt is empty', num(sw, 'SpeedPercent', ['SleepWave', 'SpeedPercent'], { min: 10, step: 10 }), '%')
        ]));

        // Platzhalter
        body.appendChild(lfPanel());
        body.appendChild(el('div', { class: 'cfg-placeholder-card' }, [
            el('h3', { text: 'Test field / dry run (placeholder)' }),
            el('div', { text: 'Will show the fleet the bot would send right now, without sending it.' })
        ]));

        // Rest generisch
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

    // ---------- Lebensform-Boni (Expeditionen) ----------
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

    // ---------- Generischer Renderer ----------
    function genericObject(obj, keys, path) {
        var wrap = el('div', { class: 'cfg-generic-group' });
        keys.forEach(function (k) {
            var v = obj[k], p = path.concat(k);
            var sn = SENS_RE.test(k) ? sens : function (x) { return x; };
            if (typeof v === 'boolean') wrap.appendChild(row(k, null, toggle(obj, k, p)));
            else if (typeof v === 'number') wrap.appendChild(row(k, null, sn(num(obj, k, p, { step: 'any' }))));
            else if (typeof v === 'string') wrap.appendChild(row(k, null, sn(text(obj, k, p))));
            else if (v && typeof v === 'object' && !Array.isArray(v)) {
                wrap.appendChild(el('div', { class: 'cfg-gname', text: k }));
                wrap.appendChild(genericObject(v, Object.keys(v), p));
            } else wrap.appendChild(row(k, 'Array/complex - edit in the JSON tab', el('code', { text: JSON.stringify(v) })));
        });
        return wrap;
    }
    function renderGeneric(body) {
        body.appendChild(sec(block, [genericObject(state, Object.keys(state), [])]));
    }

    // ---------- Rahmen ----------
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
    function update() { if (saveBtn) { saveBtn.disabled = !dirty(); dirtyLbl.textContent = dirty() ? '● unsaved changes' : ''; } }

    function maskLabel() {
        var cb = el('input', { type: 'checkbox' });
        cb.checked = mask;
        cb.addEventListener('change', function () {
            mask = cb.checked;
            try { localStorage.setItem('config.mask', mask ? '1' : '0'); } catch (e) { }
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
                if (view === 'json' && !mask) { try { state = JSON.parse(ta.value); } catch (e) { alert('Invalid JSON: ' + e.message); return; } }
                view = t; render();
            });
            tabs.appendChild(b);
        });
        root.appendChild(el('div', { class: 'cfg-head' }, [
            el('h1', { text: block }), sens(el('span', { class: 'sub grow', text: file })), maskLabel(), tabs
        ]));
        var body = el('div', { class: 'cfg-body' }), ta;
        if (view === 'form') {
            if (block === 'Expeditions') renderExpeditions(body); else renderGeneric(body);
        } else if (view === 'json') {
            ta = el('textarea', { class: 'cfg-json', spellcheck: 'false' }); ta.value = shown(state); if (mask) { ta.readOnly = true; ta.title = 'Hide data is on - switch it off to edit'; } body.appendChild(ta);
        } else body.appendChild(simpleDiff());
        root.appendChild(body);

        saveBtn = el('button', { type: 'button', class: 'cfg-btn p', text: 'Save', onclick: save });
        dirtyLbl = el('span', { class: 'dirty' });
        root.appendChild(el('div', { class: 'cfg-foot' }, [
            dirtyLbl, el('span', { class: 'grow cfg-note', text: 'Backup (.bak) is created. The bot picks up changes on its next config read/restart.' }),
            el('button', { type: 'button', class: 'cfg-btn', text: 'Discard', onclick: function () { state = clone(original); render(); } }), saveBtn
        ]));
        applyMask();
        update();
    }

    function save() {
        saveBtn.disabled = true;
        var ta = root.querySelector('.cfg-json');
        if (ta && !ta.readOnly) { try { state = JSON.parse(ta.value); } catch (e) { alert('Invalid JSON: ' + e.message); saveBtn.disabled = false; return; } }
        var fd = new URLSearchParams(); fd.set('instance', instance); fd.set('block', block); fd.set('content', JSON.stringify(state));
        fetch(main.dataset.save, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: fd.toString() })
            .then(function (r) { return r.json(); })
            .then(function (r) {
                if (!r.success) { alert('Save failed: ' + r.error); saveBtn.disabled = false; return; }
                original = clone(state); originalText = JSON.stringify(original, null, 2); render();
                dirtyLbl.textContent = '✓ saved'; dirtyLbl.style.color = 'var(--tb-ok)';
            })
            .catch(function (e) { alert('Save failed: ' + e); saveBtn.disabled = false; });
    }
})();
