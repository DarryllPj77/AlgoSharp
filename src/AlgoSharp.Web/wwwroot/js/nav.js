// Category menus in the header (<details data-catmenu>): one open at a time, closed by an outside click,
// by choosing an algorithm, or by Escape. Document-level listeners, so they survive enhanced navigation.
(() => {
    const menus = () => document.querySelectorAll('details[data-catmenu][open]');

    document.addEventListener('click', e => {
        const inside = e.target.closest && e.target.closest('details[data-catmenu]');
        menus().forEach(d => { if (d !== inside) d.open = false; });
        if (inside && e.target.closest('.catmenu-list a')) inside.open = false;
    });

    document.addEventListener('keydown', e => {
        if (e.key !== 'Escape') return;
        menus().forEach(d => { d.open = false; d.querySelector('summary')?.focus(); });
    });
})();

// When the server is full it refuses new interactive sessions (503 on the Blazor handshake). The page itself is
// already shown, but nothing on it would respond: say so plainly instead of leaving a page that silently ignores clicks.
(() => {
    if (typeof window.fetch !== 'function') return;
    const original = window.fetch;
    window.fetch = async (...args) => {
        const response = await original(...args);
        try {
            const url = typeof args[0] === 'string' ? args[0] : args[0]?.url ?? '';
            if (response.status === 503 && url.includes('/_blazor/negotiate')) showBusy(response.headers.get('Retry-After'));
        } catch { /* never interfere with the request */ }
        return response;
    };

    function showBusy(retryAfter) {
        if (document.getElementById('server-busy')) return;
        const seconds = Math.max(10, parseInt(retryAfter, 10) || 30);
        const bar = document.createElement('div');
        bar.id = 'server-busy';
        bar.setAttribute('role', 'alert');
        bar.innerHTML = '<p><b>AlgoSharp is very busy right now.</b> This page is read-only for the moment, so buttons and the editor won’t respond. ' +
            'Your saved code and drafts are safe. <button type="button">Try again</button> <span></span></p>';
        const count = bar.querySelector('span');
        let left = seconds;
        const tick = () => { count.textContent = left > 0 ? `(retrying in ${left} s)` : ''; if (left-- <= 0) location.reload(); };
        tick();
        setInterval(tick, 1000);
        bar.querySelector('button').addEventListener('click', () => location.reload());
        document.body.prepend(bar);
    }
})();
