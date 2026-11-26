// Behaviour for the server-rendered account pages (which have no Blazor circuit):
// show/hide password buttons and the theme toggle. Uses event delegation so it keeps
// working across Blazor's enhanced page navigations.
(() => {
    document.addEventListener("click", (e) => {
        const reveal = e.target.closest?.("[data-reveal]");
        if (reveal) {
            const input = document.getElementById(reveal.dataset.reveal);
            if (!input) return;
            const show = input.type === "password";
            input.type = show ? "text" : "password";
            reveal.setAttribute("aria-pressed", String(show));
            reveal.setAttribute("aria-label", show ? "Hide password" : "Show password");
            reveal.textContent = show ? "Hide" : "Show";
            return;
        }
        // Only the static (non-interactive) layout renders data-theme-toggle; interactive pages use Blazor.
        const theme = e.target.closest?.("[data-theme-toggle]");
        if (theme) {
            const next = (document.documentElement.dataset.theme || "dark") === "dark" ? "light" : "dark";
            document.documentElement.dataset.theme = next;
            try { localStorage.setItem("algosharp-theme", next); } catch { /* private mode */ }
        }
    });

    // Live "passwords match" feedback; the server re-validates everything on submit.
    document.addEventListener("input", (e) => {
        const confirm = e.target.closest?.("[data-match]");
        const target = confirm ?? document.querySelector(`[data-match="${e.target.id}"]`);
        if (!target) return;
        const other = document.getElementById(target.dataset.match);
        const note = document.getElementById(target.id + "-match");
        if (!note || !other) return;
        if (!target.value) { note.textContent = ""; return; }
        const ok = target.value === other.value;
        note.textContent = ok ? "Passwords match" : "Passwords don't match yet";
        note.className = ok ? "field-ok" : "field-hint";
    });
})();
