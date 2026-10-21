(function () {
    function esc(s) {
        return String(s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; });
    }
    function show(d) {
        var host = document.querySelector('.main-container');
        if (!host) return;
        var box = document.createElement('div');
        box.className = 'alert alert-info alert-dismissible mb-2';
        box.setAttribute('role', 'alert');
        box.style.cssText = 'margin:0 0 8px';
        box.innerHTML = '<strong>' + esc(d.title) + '</strong><ul class="mb-0 mt-1">' +
            d.lines.map(function (l) { return '<li>' + esc(l) + '</li>'; }).join('') +
            '</ul><button type="button" class="btn-close" aria-label="Close"></button>';
        box.querySelector('.btn-close').addEventListener('click', function () {
            box.remove();
            fetch('/WhatsNew/Dismiss', { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: 'version=' + encodeURIComponent(d.version) }).catch(function () { });
        });
        host.insertBefore(box, host.firstChild);
    }
    document.addEventListener('DOMContentLoaded', function () {
        fetch('/WhatsNew/Current').then(function (r) { return r.json(); }).then(function (d) {
            if (d && d.pending) show(d);
        }).catch(function () { });
    });
})();
