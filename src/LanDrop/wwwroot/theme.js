(function (root) {
  'use strict';
  function normalizePreference(value) { return ['system', 'light', 'dark'].includes(value) ? value : 'system'; }
  function resolveTheme(preference, darkSystem) { return normalizePreference(preference) === 'system' ? (darkSystem ? 'dark' : 'light') : preference; }
  const api = { normalizePreference, resolveTheme };
  if (typeof module === 'object' && module.exports) module.exports = api;
  root.ThemeUX = api;
  if (!root.document) return;
  let preference = 'system';
  try { preference = normalizePreference(root.localStorage.getItem('landrop-theme')); } catch {}
  const media = typeof root.matchMedia === 'function' ? root.matchMedia('(prefers-color-scheme: dark)') : null;
  function apply() {
    root.document.documentElement.dataset.theme = resolveTheme(preference, Boolean(media?.matches));
    const select = root.document.getElementById('theme');
    if (select) select.value = preference;
  }
  function setPreference(value) {
    preference = normalizePreference(value);
    try { root.localStorage.setItem('landrop-theme', preference); } catch {}
    apply();
  }
  api.setPreference = setPreference;
  apply();
  if (media?.addEventListener) media.addEventListener('change', () => { if (preference === 'system') apply(); });
  else if (media?.addListener) media.addListener(() => { if (preference === 'system') apply(); });
  root.document.addEventListener('DOMContentLoaded', () => {
    apply();
    const select = root.document.getElementById('theme');
    if (select) select.addEventListener('change', () => setPreference(select.value));
  });
})(typeof globalThis === 'object' ? globalThis : this);
