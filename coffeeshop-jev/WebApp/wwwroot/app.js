window.cs = {
  get: (k) => { try { return localStorage.getItem(k); } catch { return null; } },
  set: (k, v) => { try { localStorage.setItem(k, v); } catch { /* private mode: keep in memory */ } },
  isDark: () => (document.documentElement.dataset.theme || (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light")) === "dark",
  toggleTheme() {
    const next = this.isDark() ? "light" : "dark";
    document.documentElement.dataset.theme = next;
    this.set("cs.theme", JSON.stringify(next));
    return next === "dark";
  },
  focusInput: () => document.querySelector(".sc-ai-chat-page textarea, .sc-ai-root textarea")?.focus(),
  limitInput: (max) => document.querySelectorAll(".sc-ai-root textarea").forEach((t) => t.setAttribute("maxlength", String(max))),
};
