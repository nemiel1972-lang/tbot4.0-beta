(function () {
    var lang = (document.documentElement.getAttribute('data-lang') || 'en').toLowerCase();
    window.UI_LANG = lang;
    var D = window.UI_I18N && window.UI_I18N[lang];
    if (lang === 'en' || !D) { window.uiT = function (s) { return s; }; return; }
    var exact = D.exact || {}, phrases = (D.phrases || []).slice().sort(function (a, b) { return b[0].length - a[0].length; });
    var rx = (D.rx || []).map(function (r) { return [new RegExp(r[0]), r[1]]; });
    var cache = {};
    function norm(s) { return s.replace(/\s+/g, ' ').trim(); }
    function lookup(core) {
        if (Object.prototype.hasOwnProperty.call(cache, core)) return cache[core];
        var r = null;
        if (Object.prototype.hasOwnProperty.call(exact, core)) r = exact[core];
        if (r === null) for (var i = 0; i < rx.length; i++) if (rx[i][0].test(core)) { r = core.replace(rx[i][0], rx[i][1]); break; }
        if (r === null && core.length <= 60 && /^[A-Za-z0-9 ]+$/.test(core)) {
            var t = core, hit = false;
            phrases.forEach(function (p) {
                var re = new RegExp('\\b' + p[0].replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '\\b', 'g');
                if (re.test(t)) { hit = true; t = t.replace(re, '\u0001' + p[1] + '\u0002'); }
            });
            if (hit) r = t.replace(/[\u0001\u0002]/g, '');
        }
        cache[core] = r;
        return r;
    }
    function T(s) {
        if (typeof s !== 'string' || !s) return s;
        var m = /^(\s*)([\s\S]*?)(\s*)$/.exec(s);
        if (!m[2]) return s;
        var r = lookup(norm(m[2]));
        return r === null ? s : m[1] + r + m[3];
    }
    window.uiT = T;
    var SKIP = { SCRIPT: 1, STYLE: 1, TEXTAREA: 1, CODE: 0, PRE: 1, NOSCRIPT: 1 };
    var ATTRS = ['title', 'placeholder', 'alt', 'aria-label'];
    function skipEl(e) { return !e || (e.nodeType === 1 && (SKIP[e.tagName] || (e.closest && e.closest('[data-no-i18n]')))); }
    var busy = false;
    function doText(n) {
        var v = n.nodeValue, r = T(v);
        if (r !== v) n.nodeValue = r;
    }
    function doEl(e) {
        ATTRS.forEach(function (a) { if (e.hasAttribute && e.hasAttribute(a)) { var v = e.getAttribute(a), r = T(v); if (r !== v) e.setAttribute(a, r); } });
        if (e.tagName === 'INPUT' && /^(button|submit|reset)$/i.test(e.type || '')) { var v = e.value, r = T(v); if (r !== v) e.value = r; }
    }
    function walk(root) {
        if (!root || skipEl(root.nodeType === 3 ? root.parentNode : root)) return;
        if (root.nodeType === 3) { doText(root); return; }
        if (root.nodeType !== 1) return;
        doEl(root);
        for (var c = root.firstChild; c; c = c.nextSibling) {
            if (c.nodeType === 3) doText(c);
            else if (c.nodeType === 1 && !skipEl(c)) walk(c);
        }
    }
    function run(root) {
        if (busy) return;
        busy = true;
        try { walk(root); } finally { busy = false; }
    }
    function start() {
        document.title = T(document.title);
        run(document.body);
        new MutationObserver(function (muts) {
            if (busy) return;
            busy = true;
            try {
                muts.forEach(function (m) {
                    if (m.type === 'childList') m.addedNodes.forEach(function (n) { walk(n); });
                    else if (m.type === 'characterData') { if (!skipEl(m.target.parentNode)) doText(m.target); }
                    else if (m.type === 'attributes') { if (!skipEl(m.target)) doEl(m.target); }
                });
            } finally { busy = false; }
        }).observe(document.body, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ATTRS });
    }
    ['alert', 'confirm', 'prompt'].forEach(function (f) {
        var o = window[f];
        window[f] = function (msg) { var a = Array.prototype.slice.call(arguments); a[0] = T(String(msg)); return o.apply(window, a); };
    });
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
})();
