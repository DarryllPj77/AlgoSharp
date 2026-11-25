// Small browser helpers. All algorithm logic and state lives in C#; this file only
// touches things Blazor cannot reach directly (theme persistence, global keys, scrolling).

const THEME_KEY = "algosharp-theme";

export function getTheme() {
    return document.documentElement.dataset.theme || "dark";
}

export function toggleTheme() {
    const next = getTheme() === "dark" ? "light" : "dark";
    document.documentElement.dataset.theme = next;
    try { localStorage.setItem(THEME_KEY, next); } catch { /* private mode */ }
    return next;
}

export function registerShortcuts(dotnet) {
    const handler = (e) => {
        if (e.ctrlKey || e.metaKey || e.altKey) return;
        const t = e.target;
        const typing = t && t.closest && t.closest("input, textarea, select, [contenteditable]");
        if (typing) {
            if (e.key === "Escape") t.blur();
            return;
        }
        const map = { " ": "toggle", "k": "toggle", "ArrowRight": "next", "l": "next", "ArrowLeft": "prev", "j": "prev", "Home": "first", "End": "last", "r": "reset", "/": "search" };
        const action = map[e.key];
        if (!action) return;
        e.preventDefault();
        dotnet.invokeMethodAsync("OnShortcut", action);
    };
    document.addEventListener("keydown", handler);
    return { dispose: () => document.removeEventListener("keydown", handler) };
}

export function scrollActiveLine(container) {
    if (!container) return;
    const line = container.querySelector(".code-line.active");
    if (!line) return;
    const top = line.offsetTop - container.clientHeight / 2 + line.clientHeight / 2;
    container.scrollTo({ top: Math.max(0, top), behavior: "smooth" });
}

export function focusElement(el) {
    el?.focus();
}
