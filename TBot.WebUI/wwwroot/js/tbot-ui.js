(function () {
    'use strict';
    var html = document.documentElement;
    var ACCENTS = { gold: '#f5a623', blue: '#5fb4ff', green: '#4fd1a1', pink: '#f07fa5' };
    var CLASSIC = lsGet('tb.ui', 'new') === 'classic';
    var BGS = ['animated', 'static', 'off'];

    function lsGet(k, d) { try { var v = localStorage.getItem(k); return v === null ? d : v; } catch (e) { return d; } }
    function lsSet(k, v) { try { localStorage.setItem(k, v); } catch (e) { } }

    var ICONS = {
        Start: 'M3 11l9-8 9 8M5 10v10h5v-6h4v6h5V10',
        Credentials: 'M7 11V8a5 5 0 0110 0v3M5 11h14v10H5z',
        General: 'M12 15a3 3 0 100-6 3 3 0 000 6zM19 12l2-1-2-4-2 .5-1.5-1.2.3-2.3-4-1-1 2h-2l-1-2-4 1 .3 2.3L4 8.500 2 8l-2 4 2 1v2l-2 1 2 4 2-.5 1.500 1.200-.3 2.300 4 1 1-2h2l1 2 4-1-.3-2.300L19 19.500l2 .5 2-4-2-1z',
        Messenger: 'M4 5h16v11H9l-5 4z',
        SleepMode: 'M20 14a8 8 0 11-10-10 6 6 0 0010 10z',
        Defender: 'M12 3l8 3v6c0 5-4 8-8 9-4-1-8-4-8-9V6z',
        Brain: 'M9 4a3 3 0 00-3 3 3 3 0 00-2 5 3 3 0 002 5 3 3 0 006 0V7a3 3 0 00-3-3zM15 4a3 3 0 013 3 3 3 0 012 5 3 3 0 01-2 5 3 3 0 01-3 2',
        Expeditions: 'M12 3l2.500 6.500L21 12l-6.500 2.500L12 21l-2.500-6.500L3 12l6.500-2.500z',
        AutoFarm: 'M12 12m-8 0a8 8 0 1016 0 8 8 0 10-16 0M12 12m-4 0a4 4 0 108 0 4 4 0 10-8 0M12 2v4M12 18v4M2 12h4M18 12h4',
        FastFarm: 'M13 2L4 14h7l-1 8 9-12h-7z',
        AutoHarvest: 'M4 20c0-8 6-14 16-14-1 10-7 16-14 14M4 20l8-8',
        AutoColonize: 'M12 21s-7-6-7-11a7 7 0 0114 0c0 5-7 11-7 11zM12 10m-2.500 0a2.500 2.500 0 105 0 2.500 2.500 0 10-5 0',
        AutoDiscovery: 'M11 11m-7 0a7 7 0 1014 0 7 7 0 10-14 0M21 21l-5-5',
        DebrisSave: 'M5 8l7-4 7 4v8l-7 4-7-4zM5 8l7 4 7-4M12 12v8',
        AutoBidAuction: 'M14 4l6 6-3 3-6-6zM11 7L4 14l3 3 7-7M3 21h10',
        JumpGate: 'M12 3a9 9 0 100 18 9 9 0 000-18zM12 7a5 5 0 100 10 5 5 0 000-10z',
        Testing: 'M9 3h6M10 3v6l-5 9a2 2 0 002 3h10a2 2 0 002-3l-5-9V3',
        _def: 'M12 15a3 3 0 100-6 3 3 0 000 6zM12 3v3M12 18v3M3 12h3M18 12h3'
    };
    var DESC = {
        Start: 'Overview of your empire',
        Credentials: 'Login data (edit in the file)',
        General: 'Basic settings of the bot',
        Messenger: 'Telegram messages and commands',
        SleepMode: 'Quiet hours and GhostSleep',
        Defender: 'Protects planets from attacks',
        Brain: 'Builds and researches for you',
        Expeditions: 'Sends fleets into the unknown',
        AutoFarm: 'Raids inactive players',
        FastFarm: 'Quick raids with spare ships',
        AutoHarvest: 'Collects debris fields',
        AutoColonize: 'Settles new planets',
        AutoDiscovery: 'Discovery missions',
        DebrisSave: 'Saves fleets on debris fields',
        AutoBidAuction: 'Bids in the daily auction',
        JumpGate: 'Moves ships between moons',
        Testing: 'Experiments and test runs'
    };
    var CRITICAL = { Defender: 'Without the Defender nobody watches for incoming attacks and your fleets are not saved. Really switch it off?' };

    function svg(path) {
        return '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="' + path + '"/></svg>';
    }

    function applyAccent(name) {
        var c = ACCENTS[name] || ACCENTS.gold;
        html.style.setProperty('--tb-accent', c);
    }
    var space = null;
    function buildSpace() {
        if (space) return space;
        space = document.createElement('div');
        space.id = 'tb-space';
        space.setAttribute('aria-hidden', 'true');
        space.innerHTML =
            '<div class="layer a-neb"></div><div class="layer a-starsA"></div><div class="layer a-starsB"></div>' +
            '<div class="a-planet"><div class="a-ptex"></div><div class="a-shade"></div></div><div class="a-moon"></div>' +
            '<div class="a-sphere"><div class="a-sphere-in">' +
            '<div class="a-hull"><div class="a-hulltex"></div><div class="a-hullshade"></div></div>' +
            '<div class="a-panel" style="left:60px"></div><div class="a-arm" style="left:210px"></div>' +
            '<div class="a-panel" style="left:550px"></div><div class="a-arm" style="left:470px"></div>' +
            '<div class="a-mast" style="left:380px;top:20px;height:80px"></div><div class="a-beacon" style="left:377px;top:14px"></div>' +
            '</div></div><div class="a-shoot a-shoot1"></div><div class="a-shoot a-shoot2"></div>';
        document.body.insertBefore(space, document.body.firstChild);
        return space;
    }
    function applyBg(mode) {
        if (BGS.indexOf(mode) < 0) mode = 'animated';
        html.classList.toggle('tb-bg', mode !== 'off');
        html.classList.toggle('tb-bg-space', mode === 'static');
        if (mode === 'off') { if (space) space.style.display = 'none'; return; }
        buildSpace().style.display = '';
    }

    function startRocks(layer, box) {
        if (window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches) return;
        var rocks = [], W = box.clientWidth || 1000, H = box.clientHeight || 130, last = 0, MAX = 5;
        function make(spread) {
            var size = 14 + Math.random() * 20, dir = Math.random() < 0.5 ? 1 : -1;
            var el = document.createElement('div');
            el.className = 'a-met shp' + (1 + Math.floor(Math.random() * 3));
            el.style.width = size + 'px'; el.style.height = (size * 0.85) + 'px';
            layer.appendChild(el);
            rocks.push({ el: el, size: size, x: spread ? Math.random() * W : (dir > 0 ? -size : W + size), y: 6 + Math.random() * (H - size - 12),
                vx: dir * (22 + Math.random() * 28), vy: (Math.random() - 0.5) * 10, rot: Math.random() * 360, vr: (Math.random() - 0.5) * 80 });
        }
        function pop(x, y) {
            var p = document.createElement('div');
            p.className = 'a-pop'; p.style.left = x + 'px'; p.style.top = y + 'px';
            layer.appendChild(p);
            setTimeout(function () { if (p.parentNode) p.parentNode.removeChild(p); }, 800);
        }
        function drop(r) {
            if (r.el.parentNode) r.el.parentNode.removeChild(r.el);
            rocks.splice(rocks.indexOf(r), 1);
            setTimeout(function () { if (box.isConnected && rocks.length < MAX) make(false); }, 3000 + Math.random() * 5000);
        }
        var cool = new WeakMap();
        function shoot(now) {
            var fl = box.querySelectorAll('.a-fl');
            if (!fl.length || !rocks.length) return;
            var br = box.getBoundingClientRect();
            for (var i = 0; i < fl.length; i++) {
                var sh = fl[i], rc = sh.getBoundingClientRect();
                if ((cool.get(sh) || 0) > now) continue;
                var back = sh.classList.contains('fl-back');
                var nx = (back ? rc.left : rc.right) - br.left, ny = (rc.top + rc.height / 2) - br.top;
                for (var k = 0; k < rocks.length; k++) {
                    var r = rocks[k];
                    if (r.dying) continue;
                    var cx = r.x + r.size / 2, cy = r.y + r.size * 0.42, ahead = back ? nx - cx : cx - nx;
                    if (ahead > 4 && ahead < 140 && Math.abs(cy - ny) < r.size * 0.5 + 8) {
                        cool.set(sh, now + 2500 + Math.random() * 2000);
                        r.dying = true;
                        fire(nx, ny, cx, cy, sh.classList.contains('fl-att') ? 'sr' : (back ? 'sy' : 'sb'));
                        (function (rr, x, y) { setTimeout(function () { if (rocks.indexOf(rr) >= 0) { pop(x, y); drop(rr); } }, 160); })(r, cx, cy);
                        break;
                    }
                }
            }
        }
        function fire(x1, y1, x2, y2, cls) {
            var d = document.createElement('div'), len = Math.sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            d.className = 'a-shot ' + cls;
            d.style.left = x1 + 'px'; d.style.top = y1 + 'px'; d.style.width = len + 'px';
            d.style.transform = 'rotate(' + Math.atan2(y2 - y1, x2 - x1) + 'rad)';
            layer.appendChild(d);
            setTimeout(function () { if (d.parentNode) d.parentNode.removeChild(d); }, 200);
        }
        for (var i = 0; i < MAX; i++) make(true);
        function tick(t) {
            if (!box.isConnected) return;
            var dt = Math.min(0.1, (t - last) / 1000); last = t;
            if (!document.hidden && dt > 0) {
                W = box.clientWidth || W;
                box.style.setProperty('--w', W + 'px');
                rocks.slice().forEach(function (r) {
                    r.x += r.vx * dt; r.y += r.vy * dt; r.rot += r.vr * dt;
                    if (r.y < 2 || r.y > H - r.size) r.vy = -r.vy;
                    r.el.style.transform = 'translate(' + r.x.toFixed(1) + 'px,' + r.y.toFixed(1) + 'px) rotate(' + r.rot.toFixed(0) + 'deg)';
                    if ((r.vx > 0 && r.x > W + r.size) || (r.vx < 0 && r.x < -r.size * 2)) drop(r);
                });
                shoot(t);
                for (var a = 0; a < rocks.length; a++) {
                    for (var b2 = a + 1; b2 < rocks.length; b2++) {
                        var p = rocks[a], q = rocks[b2];
                        var dx = (p.x + p.size / 2) - (q.x + q.size / 2), dy = (p.y + p.size * 0.42) - (q.y + q.size * 0.42);
                        if (Math.sqrt(dx * dx + dy * dy) < (p.size + q.size) * 0.36) {
                            pop((p.x + q.x + (p.size + q.size) / 2) / 2, (p.y + q.y + (p.size + q.size) * 0.42) / 2);
                            drop(p); drop(q);
                            a = rocks.length; break;
                        }
                    }
                }
            }
            requestAnimationFrame(tick);
        }
        requestAnimationFrame(tick);
    }

    function startFleets(layer, box, main) {
        var url = main.getAttribute('data-fleetsanim'), inst = main.getAttribute('data-instance') || '', sig = null;
        if (!url) return;
        var H = box.clientHeight || 130, lanes = Math.max(1, Math.floor((H - 20) / 18));
        function render(list) {
            var kinds = list.map(function (f) { return (f.a && !f.b) ? 'att' : (f.b ? 'back' : 'out'); });
            var cnt = { out: 0, back: 0, att: 0 };
            kinds.forEach(function (k) { cnt[k]++; });
            var lab = box.querySelector('.tb-count');
            if (!lab) { lab = document.createElement('div'); lab.className = 'tb-count'; box.appendChild(lab); }
            lab.textContent = 'out ' + cnt.out + ' / back ' + cnt.back + ' / attack ' + cnt.att;
            var s2 = kinds.join(',');
            if (s2 === sig) return;
            sig = s2;
            layer.innerHTML = '';
            kinds.forEach(function (k, i) {
                var dur = 18 + (i * 7) % 14;
                var d = document.createElement('div');
                d.className = 'a-fl fl-' + k;
                d.style.top = (8 + (i % lanes) * 18) + 'px';
                d.style.animationDuration = dur + 's';
                d.style.animationDelay = (-((i * 5.3) % dur)).toFixed(1) + 's';
                var flame = k === 'att' ? 'fl-r' : (k === 'back' ? 'fl-y' : 'fl-b');
                var hull = k === 'att' ? 'a-hullr' : (k === 'back' ? 'a-hully' : 'a-hullb');
                d.innerHTML = '<div class="a-flin"><span class="a-flame ' + flame + '"></span><span class="' + hull + '"></span></div>';
                layer.appendChild(d);
            });
        }
        function poll() {
            if (document.hidden || !box.isConnected) return;
            fetch(url + (url.indexOf('?') < 0 ? '?' : '&') + 'instance=' + encodeURIComponent(inst), { cache: 'no-store' })
                .then(function (r) { return r.json(); })
                .then(function (d) { render(d && d.fleets ? d.fleets : []); alarm(d && d.attacks ? d.attacks : [], d); })
                .catch(function () { });
        }
        var baseTitle = document.title, aTimer = null, aList = [], aT0 = 0, aOver = null;
        function alarmTick() {
            if (!aOver) return;
            var el = (Date.now() - aT0) / 1000, best = null;
            aList.forEach(function (x) { var l = Math.max(0, (x.inSec || 0) - el); if (best === null || l < best) best = l; });
            var m = Math.floor(best / 60), s = Math.floor(best % 60);
            var T = window.uiT || function (x) { return x; }, t = T('Incoming attack detected');
            aOver.querySelector('b').textContent = t;
            aOver.querySelector('span').textContent = aList.length + ' x - ' + T('first impact in') + ' ' + (m < 10 ? '0' : '') + m + ':' + (s < 10 ? '0' : '') + s;
        }
        function alarm(list, d) {
            if (list.length === 0) {
                box.classList.remove('tb-alarm');
                if (aOver && aOver.parentNode) aOver.parentNode.removeChild(aOver);
                aOver = null; if (aTimer) { clearInterval(aTimer); aTimer = null; }
                document.title = baseTitle; return;
            }
            aList = list; aT0 = Date.now() - Math.max(0, ((d && d.nowMs) || 0) - ((d && d.updatedMs) || 0));
            box.classList.add('tb-alarm');
            if (!aOver) {
                aOver = document.createElement('div'); aOver.className = 'tb-alarmtxt';
                aOver.innerHTML = '<b></b><span></span>';
                box.appendChild(aOver);
            }
            document.title = '\u26A0 ' + (window.uiT ? window.uiT('Incoming attack detected') : 'Incoming attack detected');
            alarmTick();
            if (!aTimer) aTimer = setInterval(alarmTick, 1000);
        }
        poll();
        setInterval(poll, 10000);
    }

    function buildBattle() {
        var main = document.getElementById('cfg-main');
        if (!main || document.getElementById('tb-battle')) return;
        var b = document.createElement('div');
        b.id = 'tb-battle'; b.className = 'tb-battle'; b.setAttribute('aria-hidden', 'true');
        b.innerHTML = '<div class="tb-rocks"></div><div class="tb-fleets"></div>';
        var grid = main.parentNode;
        grid.parentNode.insertBefore(b, grid);
        b.style.setProperty('--w', (b.clientWidth || 1000) + 'px');
        startRocks(b.querySelector('.tb-rocks'), b);
        startFleets(b.querySelector('.tb-fleets'), b, main);
    }

    function decorateMenu() {
        var list = document.getElementById('cfg-blocks');
        if (!list) return;
        var items = list.querySelectorAll('li.cfg-block');
        Array.prototype.forEach.call(items, function (li) {
            if (li.dataset.tbui) return;
            li.dataset.tbui = '1';
            var name = li.dataset.name;
            var a = li.querySelector('a');
            var sw = li.querySelector('.sw');
            var path = ICONS[name] || ICONS._def;
            if (!sw) { sw = document.createElement('span'); sw.className = 'sw nosw'; sw.title = 'Nothing to switch here'; }
            else if (sw.parentNode === a) { a.removeChild(sw); }
            sw.innerHTML = svg(path);
            li.appendChild(sw);
            var d = DESC[name];
            if (d && a) { var s = document.createElement('span'); s.className = 'desc'; s.textContent = d; a.appendChild(s); }
            if (CRITICAL[name]) li.classList.add('crit');
        });
        if (!document.getElementById('tb-legend')) {
            var lg = document.createElement('div');
            lg.id = 'tb-legend'; lg.className = 'tb-legend';
            lg.innerHTML = '<div><i class="on"></i><span>Lit tile: worker is on duty</span></div>' +
                '<div><i></i><span>Dark tile: worker is resting</span></div>' +
                '<div><i class="dash"></i><span>Dashed tile: nothing to switch</span></div>' +
                '<div><b>!</b><span>Yellow name: critical worker, asks before switching off</span></div>' +
                '<div><i class="fb"></i><span>Blue ship: fleet on its way out</span></div>' +
                '<div><i class="fy"></i><span>Yellow ship: fleet returning</span></div>' +
                '<div><i class="fr"></i><span>Red blinking ship: attack flight</span></div>';
            list.parentNode.insertBefore(lg, list.nextSibling);
        }
    }

    document.addEventListener('click', function (e) {
        if (CLASSIC) return;
        var sw = e.target.closest && e.target.closest('#cfg-blocks .sw.click.on');
        if (!sw || sw.dataset.tbOk === '1') { if (sw) sw.dataset.tbOk = ''; return; }
        var msg = CRITICAL[sw.dataset.block];
        if (!msg) return;
        e.stopImmediatePropagation(); e.preventDefault();
        var old = document.getElementById('tb-confirm'); if (old) old.remove();
        var box = document.createElement('div');
        box.id = 'tb-confirm'; box.className = 'tb-confirm'; box.setAttribute('role', 'alertdialog');
        var t = document.createElement('div'); t.className = 'txt'; t.textContent = msg;
        var yes = document.createElement('button'); yes.type = 'button'; yes.className = 'yes'; yes.textContent = 'Yes, switch off';
        var no = document.createElement('button'); no.type = 'button'; no.className = 'no'; no.textContent = 'Cancel';
        box.appendChild(t); box.appendChild(yes); box.appendChild(no);
        var main = document.getElementById('cfg-main');
        main.parentNode.parentNode.insertBefore(box, main.parentNode);
        no.onclick = function () { box.remove(); };
        yes.onclick = function () { box.remove(); sw.dataset.tbOk = '1'; sw.click(); };
        box.scrollIntoView({ block: 'nearest' });
    }, true);

    function buildLook() {
        var langsel = document.getElementById('ui-langsel');
        if (!langsel || document.getElementById('tb-look')) return;
        var wrap = document.createElement('span');
        wrap.id = 'tb-look'; wrap.className = 'tb-look';
        var ui = document.createElement('select'); ui.title = 'Interface'; ui.setAttribute('aria-label', 'Interface');
        if (CLASSIC) ui.className = 'form-select form-select-sm w-auto me-2';
        [['new', 'New interface'], ['classic', 'Classic interface']].forEach(function (o) { var op = document.createElement('option'); op.value = o[0]; op.textContent = o[1]; ui.appendChild(op); });
        ui.value = CLASSIC ? 'classic' : 'new';
        ui.onchange = function () { lsSet('tb.ui', ui.value); location.reload(); };
        wrap.appendChild(ui);
        if (CLASSIC) { langsel.parentNode.insertBefore(wrap, langsel); return; }
        var acc = document.createElement('select'); acc.title = 'Accent colour'; acc.setAttribute('aria-label', 'Accent colour');
        [['gold', 'Gold'], ['blue', 'Blue'], ['green', 'Green'], ['pink', 'Pink']].forEach(function (o) { var op = document.createElement('option'); op.value = o[0]; op.textContent = o[1]; acc.appendChild(op); });
        var bg = document.createElement('select'); bg.title = 'Background'; bg.setAttribute('aria-label', 'Background');
        [['animated', 'Space (moving)'], ['static', 'Space (still)'], ['off', 'Plain']].forEach(function (o) { var op = document.createElement('option'); op.value = o[0]; op.textContent = o[1]; bg.appendChild(op); });
        acc.value = lsGet('tb.accent', 'gold'); bg.value = lsGet('tb.bg', 'animated');
        acc.onchange = function () { lsSet('tb.accent', acc.value); applyAccent(acc.value); };
        bg.onchange = function () { lsSet('tb.bg', bg.value); applyBg(bg.value); };
        wrap.appendChild(acc); wrap.appendChild(bg);
        langsel.parentNode.insertBefore(wrap, langsel);
    }

    function markNav() {
        var path = location.pathname.toLowerCase();
        document.querySelectorAll('.navbar .nav-link').forEach(function (a) {
            var h = (a.getAttribute('href') || '').toLowerCase();
            var also = (a.getAttribute('data-also') || '').toLowerCase().split(',');
            if ((h && h !== '/' && path.indexOf(h) === 0) || also.some(function (x) { return x && path.indexOf(x) === 0; })) a.classList.add('tb-cur');
        });
    }

    function init() {
        if (CLASSIC) { buildLook(); return; }
        applyAccent(lsGet('tb.accent', 'gold'));
        applyBg(lsGet('tb.bg', 'animated'));
        buildLook(); markNav(); decorateMenu(); buildBattle();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
