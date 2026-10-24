(() => {
    "use strict";
    const toggle = document.querySelector("[data-nav-toggle]");
    const panel = document.querySelector("[data-nav-panel]");
    const backdrop = document.querySelector("[data-nav-backdrop]");
    const desktop = window.matchMedia("(min-width: 1024px)");
    const setNavigation = (open, restoreFocus = false) => {
        if (!panel || !toggle || !backdrop) return;
        panel.classList.toggle("hidden", !open);
        panel.classList.toggle("flex", open);
        toggle.setAttribute("aria-expanded", String(open));
        toggle.setAttribute("aria-label", open ? "Close navigation" : "Open navigation");
        backdrop.hidden = !open;
        document.body.style.overflow = open && !desktop.matches ? "hidden" : "";
        if (open && !desktop.matches) panel.querySelector("a, button")?.focus();
        if (!open && restoreFocus) toggle.focus();
    };
    toggle?.addEventListener("click", () => setNavigation(toggle.getAttribute("aria-expanded") !== "true"));
    backdrop?.addEventListener("click", () => setNavigation(false, true));
    document.querySelector("[data-nav-close]")?.addEventListener("click", () => setNavigation(false, true));
    desktop.addEventListener("change", () => setNavigation(false));
    document.addEventListener("keydown", event => {
        if (desktop.matches || toggle?.getAttribute("aria-expanded") !== "true") return;
        if (event.key === "Escape") setNavigation(false, true);
        if (event.key === "Tab" && panel) {
            const elements = [...panel.querySelectorAll("a[href], button:not(:disabled)")].filter(element => element.getClientRects().length);
            const first = elements[0], last = elements[elements.length - 1];
            if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
            else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
        }
    });
    document.querySelectorAll("[data-dismiss-notice]").forEach(button => button.addEventListener("click", () => button.closest("[data-notice]")?.remove()));
    document.querySelectorAll("[data-password-toggle]").forEach(button => button.addEventListener("click", () => {
        const field = document.getElementById(button.getAttribute("aria-controls"));
        if (!field) return;
        const visible = field.type === "password";
        field.type = visible ? "text" : "password";
        button.setAttribute("aria-pressed", String(visible));
        button.textContent = visible ? "Hide" : "Show";
    }));
    document.querySelectorAll('form[method="post"]').forEach(form => form.addEventListener("submit", event => {
        if (event.defaultPrevented || !form.checkValidity()) return;
        if (window.jQuery?.validator && !window.jQuery(form).valid()) return;
        if (form.dataset.submitting === "true") { event.preventDefault(); return; }
        form.dataset.submitting = "true";
        const button = event.submitter;
        if (button) { button.setAttribute("aria-disabled", "true"); button.classList.add("opacity-60", "cursor-wait"); }
    }));
    window.addEventListener("pageshow", () => document.querySelectorAll("form[data-submitting]").forEach(form => {
        delete form.dataset.submitting;
        form.querySelectorAll('[aria-disabled="true"]').forEach(button => { button.removeAttribute("aria-disabled"); button.classList.remove("opacity-60", "cursor-wait"); });
    }));
})();
