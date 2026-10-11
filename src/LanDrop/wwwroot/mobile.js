// Platform choices affect instructions and suggested names only, never access control.
globalThis.MobileUX = Object.freeze({
  detectPlatform(nav = {}) {
    const ua = String(nav.userAgent || '');
    if (/Android/i.test(ua)) return 'android';
    if (/iPhone|iPad|iPod/i.test(ua) || (nav.platform === 'MacIntel' && nav.maxTouchPoints > 1)) return 'ios';
    return 'other';
  },
  normalizePreference(value) { return ['auto', 'android', 'ios', 'other'].includes(value) ? value : 'auto'; },
  resolvePlatform(preference, nav) {
    const selected = this.normalizePreference(preference);
    return selected === 'auto' ? this.detectPlatform(nav) : selected;
  },
  defaultDeviceName(platform, language) {
    const names = language === 'cs' ? {android:'Můj Android', ios:'Můj iPhone', other:'Můj telefon'} : {android:'My Android', ios:'My iPhone', other:'My phone'};
    return names[platform] || names.other;
  },
  isDefaultDeviceName(name) {
    return ['My Android', 'Můj Android', 'My iPhone', 'Můj iPhone', 'My phone', 'Můj telefon'].includes(name);
  },
  uploadCapabilities(env) {
    const missing = [];
    for (const name of ['Worker', 'AbortController', 'XMLHttpRequest', 'fetch']) {
      if (typeof env[name] !== 'function') missing.push(name);
    }
    for (const name of ['slice', 'arrayBuffer']) {
      if (typeof env.Blob?.prototype?.[name] !== 'function') missing.push('Blob.' + name);
    }
    return {supported:missing.length === 0, missing};
  }
});
