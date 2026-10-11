const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const context = vm.createContext({});
vm.runInContext(fs.readFileSync(path.join(__dirname, '../src/LanDrop/wwwroot/mobile.js'), 'utf8'), context);
const ux = context.MobileUX;
assert.equal(ux.detectPlatform({userAgent:'Mozilla/5.0 (Linux; Android 15; Pixel 7) AppleWebKit/537.36 Chrome/133 Mobile Safari/537.36'}), 'android');
assert.equal(ux.detectPlatform({userAgent:'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)'}), 'ios');
assert.equal(ux.detectPlatform({userAgent:'Mozilla/5.0 (Macintosh)', platform:'MacIntel', maxTouchPoints:5}), 'ios');
assert.equal(ux.detectPlatform({userAgent:'Mozilla/5.0 (Macintosh)', platform:'MacIntel', maxTouchPoints:0}), 'other');
assert.equal(ux.detectPlatform({}), 'other');
assert.equal(ux.normalizePreference('invalid'), 'auto');
assert.equal(ux.resolvePlatform('ios', {userAgent:'Android'}), 'ios');
assert.equal(ux.resolvePlatform('android', {userAgent:'iPhone'}), 'android');
assert.equal(ux.resolvePlatform('auto', {userAgent:'Android'}), 'android');
for(const platform of ['android', 'ios', 'other']) for(const language of ['en', 'cs'])
  assert.equal(ux.isDefaultDeviceName(ux.defaultDeviceName(platform, language)), true);
assert.equal(ux.isDefaultDeviceName('My iPhone'), true, 'Legacy default remains recognized');
assert.equal(ux.isDefaultDeviceName('Pixel – Alice'), false, 'Custom names must survive language/platform changes');
const supported = {Worker(){}, AbortController(){}, XMLHttpRequest(){}, fetch(){}, Blob:{prototype:{slice(){}, arrayBuffer(){}}}};
assert.equal(ux.uploadCapabilities(supported).supported, true);
for(const capability of ['Worker','AbortController','XMLHttpRequest','fetch']) {
  const result = ux.uploadCapabilities({...supported, [capability]:undefined});
  assert.equal(result.supported, false); assert.equal(result.missing.includes(capability), true);
}
assert.equal(ux.uploadCapabilities({...supported, Blob:{prototype:{slice(){}}}}).missing.includes('Blob.arrayBuffer'), true);
assert.equal(ux.uploadCapabilities({}).supported, false);
console.log('PASS: Android/iOS/iPad desktop UA detection, explicit overrides, EN/CS suggested names/custom-name protection, individual upload capability guards.');
