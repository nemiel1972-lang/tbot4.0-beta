(function () {
    function pad(n) { return (n < 10 ? '0' : '') + n; }
    function zone(off) { var a = Math.abs(off); return 'UTC' + (off < 0 ? '-' : '+') + Math.floor(a / 60) + (a % 60 ? ':' + pad(a % 60) : ''); }
    function hm(min) { min = ((min % 1440) + 1440) % 1440; return pad(Math.floor(min / 60)) + ':' + pad(min % 60); }
    function parseHm(v) { var m = /^\s*(\d{1,2}):(\d{2})\s*$/.exec(v || ''); return m ? (+m[1]) * 60 + (+m[2]) : null; }

    window.tbotTime = function (clock) {
        var m = /(\d+):(\d+):\d+$/.exec(clock && clock.game || '');
        if (!m) return null;
        var gameMin = (+m[1]) * 60 + (+m[2]);
        var bOff = -new Date().getTimezoneOffset();
        var diff = bOff - clock.offsetMin;
        var h = Math.abs(diff) / 60;
        return {
            game: hm(gameMin), gameZone: zone(clock.offsetMin),
            browser: hm(gameMin + diff), browserZone: zone(bOff), diffMin: diff,
            diffText: diff === 0 ? 'Bot time and browser time are the same.' : 'Your time = bot time ' + (diff > 0 ? '+ ' : '- ') + (h % 1 ? h.toFixed(2) : h) + ' h.'
        };
    };

    window.tbotTimeHint = function (node, pairs) {
        var main = document.getElementById('cfg-main');
        var url = main && main.dataset.startdata, instance = main && main.dataset.instance;
        node.textContent = 'The times here are bot time (game server time).';
        if (!url) return;
        fetch(url + '?instance=' + encodeURIComponent(instance), { cache: 'no-store' })
            .then(function (r) { return r.json(); })
            .then(function (d) {
                var clock = d && d.status && d.status.clock, t = window.tbotTime(clock);
                if (!t) return;
                var txt = 'The times here are bot time (game server time). Now: ' + t.game + ' (' + t.gameZone + ') bot time, ' + t.browser + ' (' + t.browserZone + ') your browser time. ' + t.diffText;
                (pairs || []).forEach(function (p) {
                    var v = parseHm(p[1]());
                    if (v !== null) txt += ' ' + p[0] + ' ' + hm(v) + ' = ' + hm(v + t.diffMin) + ' your time.';
                });
                node.textContent = txt;
            })
            .catch(function () { });
    };
})();
