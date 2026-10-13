const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname,'../src/LanDrop/wwwroot/theme.js'),'utf8');
const {normalizePreference,resolveTheme} = require('../src/LanDrop/wwwroot/theme.js');
for(const value of [undefined,null,'',false,0,{},[],'LIGHT','invalid']) {
  assert.equal(normalizePreference(value),'system');
  assert.equal(resolveTheme(value,true),'dark');
  assert.equal(resolveTheme(value,false),'light');
}
for(const preference of ['system','light','dark']) {
  assert.equal(normalizePreference(preference),preference);
  for(const dark of [true,false]) assert.equal(resolveTheme(preference,dark),preference==='system' ? (dark?'dark':'light') : preference);
}
// Browsers can deny storage (private/managed environments). Rendering and a
// session-only manual selection must continue without an uncaught exception.
const document = {documentElement:{dataset:{}},getElementById:()=>null,addEventListener:()=>{}};
const context = {document,localStorage:{getItem(){throw new Error('storage denied');},setItem(){throw new Error('storage denied');}},matchMedia:()=>({matches:true,addEventListener(){}})};
vm.runInNewContext(source,context);
assert.equal(document.documentElement.dataset.theme,'dark');
context.ThemeUX.setPreference('light');
assert.equal(document.documentElement.dataset.theme,'light');
context.ThemeUX.setPreference('invalid');
assert.equal(document.documentElement.dataset.theme,'dark');
console.log('Theme checks passed: preference normalization, manual/system resolution and denied-storage rendering.');
