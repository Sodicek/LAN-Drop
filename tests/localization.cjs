// Run from any directory: node tests/localization.cjs
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const context = {localStorage: {getItem: () => null}, navigator: {language: 'cs-CZ'}};
vm.createContext(context);
vm.runInContext(read('src/LanDrop/wwwroot/i18n.js') + '\nglobalThis.catalog = translations;', context);
const placeholders = value => [...value.matchAll(/\{([^{}]+)\}/g)].map(match => match[1]).sort();
function parity(en, cs, name) {
  assert.deepEqual(Object.keys(en).sort(), Object.keys(cs).sort(), `${name}: key parity`);
  for (const key of Object.keys(en)) {
    assert.equal(typeof en[key], 'string', `${name}.${key}: English string`);
    assert.equal(typeof cs[key], 'string', `${name}.${key}: Czech string`);
    assert.ok(en[key].trim() && cs[key].trim(), `${name}.${key}: nonempty translations`);
    assert.deepEqual(placeholders(en[key]), placeholders(cs[key]), `${name}.${key}: placeholders`);
  }
}
parity(context.catalog.en, context.catalog.cs, 'Web');
for (const match of read('src/LanDrop/wwwroot/index.html').matchAll(/data-i18n="([^"]+)"/g)) {
  assert.ok(context.catalog.en[match[1]], `HTML translation key: ${match[1]}`);
}
for (const match of read('src/LanDrop/wwwroot/app.js').matchAll(/\bt\('([^']+)'(?!\s*\+)/g)) {
  assert.ok(context.catalog.en[match[1]], `JavaScript translation key: ${match[1]}`);
}
for(const state of ['queued','preparing','waiting','transferring','verifying','completed','failed','cancelled','pending','approved','rejected','expired']) {
  assert.ok(context.catalog.en['state_'+state] && context.catalog.cs['state_'+state], `Dynamic transfer-state translation: ${state}`);
}
for (const match of read('src/LanDrop/Program.cs').matchAll(/Error\("([^"]+)"/g)) {
  assert.ok(context.catalog.en[match[1]] && context.catalog.cs[match[1]], `Server error translation: ${match[1]}`);
}
parity(JSON.parse(read('src/LanDrop.Desktop/Locales/en.json')), JSON.parse(read('src/LanDrop.Desktop/Locales/cs.json')), 'Desktop');
assert.equal(vm.runInContext('locale()', context), 'en', 'English default');
vm.runInContext("languagePreference = 'auto'", context);
assert.equal(vm.runInContext('locale()', context), 'cs', 'Automatic Czech browser preference');
context.navigator.language = 'de-DE';
assert.equal(vm.runInContext('locale()', context), 'en', 'Unsupported browser language falls back to English');
vm.runInContext("languagePreference = 'cs'", context);
assert.equal(vm.runInContext("errorMessage({code:'recipient_required',error:'English fallback'},400)", context), context.catalog.cs.recipient_required);
assert.equal(vm.runInContext("t('operationFailed',{status:403})", context), 'Operace se nepodařila (403).');
assert.equal(vm.runInContext("errorMessage({code:'future_error',error:'Future error'},400)", context), 'Future error');
console.log('Localization checks passed: web/desktop parity, placeholders, HTML/JS keys, server error codes, language selection and fallback.');
