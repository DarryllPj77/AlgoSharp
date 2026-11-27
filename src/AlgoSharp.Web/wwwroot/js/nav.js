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
