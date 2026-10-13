// Mobile browser emulation checks real engines, browser APIs and server transfers.
// Desktop Chromium/WebKit do not verify physical Android, Android WebView or iOS Safari.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const {spawn} = require('node:child_process');
const {createHash} = require('node:crypto');
const {chromium, webkit, devices} = require('@playwright/test');
const profiles = {
  android: {engine:chromium, device:'Pixel 7', platform:'android', label:'Android', defaultEn:'My Android', defaultCs:'Můj Android'},
  iphone: {engine:webkit, device:'iPhone 13', platform:'ios', label:'iPhone', defaultEn:'My iPhone', defaultCs:'Můj iPhone'}
};
const root = path.resolve(__dirname, '..');
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function until(check, label) {
  const deadline = Date.now() + 20000;
  while(Date.now() < deadline) { const value = await check(); if(value) return value; await delay(100); }
  throw new Error('Timed out: ' + label);
}
async function main(profileName) {
  const profile = profiles[profileName];
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'landrop-mobile-'));
  const listener = net.createServer();
  await new Promise(resolve => listener.listen(0, '127.0.0.1', resolve));
  const port = listener.address().port;
  const base = `http://127.0.0.1:${port}`;
  const tlsCertificate = process.env.LAN_DROP_TEST_TLS_PFX;
  let httpsPort;
  if(tlsCertificate) {
    const tlsListener = net.createServer();
    await new Promise(resolve => tlsListener.listen(0, '127.0.0.1', resolve));
    httpsPort = tlsListener.address().port;
    await new Promise(resolve => tlsListener.close(resolve));
    assert.notEqual(httpsPort, port);
  }
  await new Promise(resolve => listener.close(resolve));
  const browserBase = tlsCertificate ? `https://127.0.0.1:${httpsPort}` : base;
  let output = '', browser, adminKey;
  const serverArguments = [path.join(root, 'src/LanDrop/bin/Release/net10.0/LanDrop.dll'),
    `--Port=${port}`, '--OpenBrowser=false', '--ListenAddress=127.0.0.1', `--DataDirectory=${temporary}`];
  if(tlsCertificate) serverArguments.push(`--HttpsPort=${httpsPort}`, `--HttpsCertificatePath=${tlsCertificate}`);
  const server = spawn('dotnet', serverArguments,
    {cwd:path.join(root, 'src/LanDrop'), windowsHide:true, stdio:['ignore','pipe','pipe']});
  server.stdout.on('data', chunk => output += chunk);
  server.stderr.on('data', chunk => output += chunk);
  let exited = false; server.on('exit', () => exited = true);
  try {
    adminKey = await until(() => {
      if(exited) throw new Error(output);
      return output.match(/#admin=([a-f0-9]+)/)?.[1];
    }, 'server startup');
    const admin = {'X-LanDrop-Admin':adminKey};
    async function api(endpoint, method='GET', value, headers=admin) {
      const response = await fetch(base + endpoint, {method, headers:{...headers, 'Content-Type':'application/json'},
        body:value === undefined ? undefined : JSON.stringify(value)});
      const body = await response.text();
      assert.equal(response.status, 200, `${endpoint}: ${body}`);
      return body ? JSON.parse(body) : null;
    }
    const inbox = path.join(temporary, 'Inbox');
    await api('/api/admin/folder', 'POST', {path:inbox});
    async function completedNames() {
      const entries = await fs.readdir(inbox, {withFileTypes:true});
      for(const entry of entries) assert.ok(entry.isFile() || (entry.isDirectory() && /^\.landrop-incoming-[a-f0-9]{32}$/.test(entry.name)), 'Known inbox entries only');
      return entries.filter(entry => entry.isFile()).map(entry => entry.name);
    }
    async function noPendingBytes() {
      for(const entry of await fs.readdir(inbox, {withFileTypes:true})) {
        if(entry.isDirectory()) assert.deepEqual((await fs.readdir(path.join(inbox,entry.name))).sort(), ['.landrop-lease','.landrop-owner'], 'No staged upload bytes before consent/cancel/reject');
      }
    }
    const qr = await api('/api/admin/qr?address=127.0.0.1');
    assert.equal(new URL(qr.link).origin, browserBase);
    browser = await profile.engine.launch();
    const context = await browser.newContext({...devices[profile.device], locale:'en-US', acceptDownloads:true});
    const page = await context.newPage();
    async function chooseFiles(files) {
      await until(() => page.locator('#file-input').isEnabled(), 'file picker enabled after prior cleanup');
      await page.locator('#file-input').setInputFiles(files);
    }
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.goto(qr.link);
    assert.equal(await page.locator('#platform').inputValue(), 'auto');
    assert.equal(await page.locator('#device-name').inputValue(), profile.defaultEn);
    await page.locator('#language').selectOption('cs');
    assert.equal(await page.locator('#device-name').inputValue(), profile.defaultCs);
    const deviceName = profile.label + ' – QA';
    await page.locator('#device-name').fill(deviceName);
    await page.locator('#platform').selectOption('ios');
    assert.equal(await page.locator('#device-name').inputValue(), deviceName);
    await page.locator('#platform').selectOption(profile.platform);
    await page.locator('#language').selectOption('en');
    await page.locator('#pair-form button').click();
    const device = await until(async () => (await api('/api/admin')).devices.find(d => d.name === deviceName), 'mobile pairing');
    await page.reload();
    await page.locator('#waiting').waitFor({state:'visible'});
    assert.equal(await page.locator('#transfer').isVisible(), false, 'Unapproved phone cannot select files');
    await api(`/api/admin/devices/${device.id}/approve`, 'POST');
    await page.locator('#transfer').waitFor({state:'visible'});
    assert.equal(await page.locator('#transport-notice').textContent(), tlsCertificate
      ? 'HTTPS connection: file transfers to this computer are encrypted.'
      : 'HTTP preview: network transfers are unencrypted. Use trusted private networks and non-sensitive test files.');
    assert.equal(await page.locator('#file-input').isEnabled(), true);
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, 'Mobile page fits viewport');
    const portrait = devices[profile.device].viewport;
    await page.setViewportSize({width:portrait.height, height:portrait.width});
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, 'Landscape page fits viewport');
    await page.setViewportSize(portrait);
    await page.reload();
    await page.locator('#transfer').waitFor({state:'visible'});
    await page.locator('#language').selectOption('cs');
    assert.equal(await page.locator('#file-input').isEnabled(), true);
    await page.locator('#language').selectOption('en');
    const payload = Buffer.from('Android browser upload\nŽluťoučký kůň 🐎\n'.repeat(2000));
    const name = 'Android – příloha 🐎.txt';
    await chooseFiles({name, mimeType:'text/plain', buffer:payload});
    const batch = await until(async () => (await api('/api/admin')).batches.find(b => b.files.some(o => o.name === name)), 'browser hash worker and batch');
    assert.equal(batch.files[0].size, payload.length);
    assert.equal((await completedNames()).length, 0, 'Nothing is saved before PC accepts');
    await noPendingBytes();
    await api(`/api/admin/offer-batches/${batch.id}/approve`, 'POST');
    await page.locator('#history .row').first().waitFor();
    assert.deepEqual(await fs.readFile(path.join(inbox, name)), payload);
    const received = (await api('/api/admin')).files.find(f => f.name === name);
    assert.equal(received.sha256, createHash('sha256').update(payload).digest('hex'));
    assert.equal(received.verified, true);
    // Cancelling a second offer must not write a file or consume PC storage.
    await chooseFiles({name:'cancelled-android.txt', mimeType:'text/plain', buffer:Buffer.from('do not save')});
    await until(async () => (await api('/api/admin')).batches.find(b => b.files.some(o => o.name === 'cancelled-android.txt')), 'cancel batch');
    await page.locator('#cancel').click();
    await until(async () => !(await api('/api/admin')).batches.some(b => b.files.some(o => o.name === 'cancelled-android.txt')), 'batch cancelled');
    assert.deepEqual(await completedNames(), [name]);
    await noPendingBytes();
    await page.locator('#retry').waitFor({state:'visible'});
    await page.locator('#clear-queue').click();
    assert.equal(await page.locator('#queue-list .row').count(), 0);
    assert.equal(await page.locator('#retry-area').isVisible(), false);
    // A rejected batch retains the selection until the user explicitly clears it.
    await chooseFiles({name:'rejected.txt', mimeType:'text/plain', buffer:Buffer.from('rejected')});
    const rejected = await until(async () => (await api('/api/admin')).batches.find(b => b.files.some(o => o.name === 'rejected.txt')), 'reject batch');
    await api(`/api/admin/offer-batches/${rejected.id}/reject`, 'POST');
    await page.locator('#queue-list [data-state="failed"]').waitFor();
    await page.locator('#retry').waitFor({state:'visible'});
    assert.deepEqual(await completedNames(), [name]);
    await noPendingBytes();
    await page.locator('#clear-queue').click();
    // Twenty selected photos require one PC consent, then preserve every file.
    const photos = Array.from({length:20}, (_, i) => ({name:`batch-photo-${i}.txt`, mimeType:'text/plain', buffer:Buffer.from(`photo ${i} 🐎`)}));
    await chooseFiles(photos);
    const photoBatch = await until(async () => (await api('/api/admin')).batches.find(b => b.files.length === 20), '20-file batch');
    assert.equal(photoBatch.totalSize, photos.reduce((sum, p) => sum + p.buffer.length, 0));
    assert.equal((await completedNames()).length, 1, 'Batch saves no bytes before consent');
    await noPendingBytes();
    await api(`/api/admin/offer-batches/${photoBatch.id}/approve`, 'POST');
    await until(async () => (await completedNames()).length === 21, '20 uploaded photos');
    await until(async () => await page.locator('#queue-list [data-state="completed"]').count() === 20, '20 verified browser completions');
    for(const photo of photos) assert.deepEqual(await fs.readFile(path.join(inbox, photo.name)), photo.buffer);
    // Take the browser offline on the second PUT: the first file must not be retried.
    let interrupted = false;
    const failOnce = async route => {
      if(!interrupted && route.request().method() === 'PUT' && new URL(route.request().url()).searchParams.get('name') === 'retry-android.txt') {
        interrupted = true; await context.setOffline(true); await route.abort('connectionfailed');
      }
      else await route.continue();
    };
    await page.route('**/api/files?*', failOnce);
    const retained = {name:'retry-android.txt', mimeType:'text/plain', buffer:Buffer.from('retry retained bytes')};
    const completed = {name:'completed-before-offline.txt', mimeType:'text/plain', buffer:Buffer.from('must be sent only once')};
    await chooseFiles([completed, retained]);
    const failedBatch = await until(async () => (await api('/api/admin')).batches.find(b => b.files.some(o => o.name === retained.name)), 'retry initial consent');
    await api(`/api/admin/offer-batches/${failedBatch.id}/approve`, 'POST');
    await page.locator('#retry').waitFor({state:'visible'});
    assert.equal(interrupted, true);
    assert.equal((await fs.readdir(inbox)).includes(retained.name), false);
    assert.deepEqual(await fs.readFile(path.join(inbox, completed.name)), completed.buffer);
    assert.equal(await page.locator('#queue-list [data-state="completed"]').count(), 1);
    await context.setOffline(false);
    await page.unroute('**/api/files?*', failOnce);
    await page.locator('#retry').click();
    const retryBatch = await until(async () => (await api('/api/admin')).batches.find(b => b.id !== failedBatch.id && b.files.some(o => o.name === retained.name)), 'new consent without file reselection');
    assert.deepEqual(retryBatch.files.map(f=>f.name), [retained.name], 'Retry requests only the unfinished file');
    await api(`/api/admin/offer-batches/${retryBatch.id}/approve`, 'POST');
    await until(async () => (await fs.readdir(inbox)).includes(retained.name), 'retained file uploaded');
    await until(async () => await page.locator('#queue-list [data-state="completed"]').count() === 2, 'retry saved receipts');
    assert.deepEqual(await fs.readFile(path.join(inbox, retained.name)), retained.buffer);
    assert.equal((await api('/api/admin')).files.filter(f=>f.name === completed.name).length, 1, 'Completed file has one saved copy');
    // Exercise worker chunk boundaries with an empty file and a synthetic large binary.
    const large = Buffer.alloc(12 * 1024 * 1024, 0xa7);
    const extra = [{name:'empty.bin', mimeType:'application/octet-stream', buffer:Buffer.alloc(0)},
      {name:'synthetic-video.bin', mimeType:'application/octet-stream', buffer:large}];
    await chooseFiles(extra);
    const extraBatch = await until(async () => (await api('/api/admin')).batches.find(b => b.files.some(f=>f.name === 'synthetic-video.bin')), 'binary batch');
    await api(`/api/admin/offer-batches/${extraBatch.id}/approve`, 'POST');
    await until(async () => await page.locator('#queue-list [data-state="completed"]').count() === 2, 'binary receipts');
    for(const file of extra) assert.deepEqual(await fs.readFile(path.join(inbox, file.name)), file.buffer);
    // Outbound download uses the phone's cookie and preserves UTF-8 filename/bytes.
    const outgoingName = 'PC – fotografie č. 1.bin';
    const outgoing = Buffer.from([0,1,2,255,128,64,0,9]);
    const prepared = await fetch(base + `/api/files?name=${encodeURIComponent(outgoingName)}&recipient=${device.id}`, {
      method:'PUT', headers:{...admin, 'Content-Type':'application/octet-stream', 'X-LanDrop-SHA256':createHash('sha256').update(outgoing).digest('hex')}, body:outgoing});
    assert.equal(prepared.status, 200, await prepared.text());
    const downloadLink = page.locator('#files a').filter({hasText:'Download'});
    await downloadLink.waitFor();
    const downloadResponse = await context.request.get(new URL(await downloadLink.getAttribute('href'), browserBase).href);
    assert.equal(downloadResponse.status(), 200);
    const disposition = downloadResponse.headers()['content-disposition'];
    const encodedName = disposition.match(/filename\*=UTF-8''([^;]*)/i)?.[1];
    assert.equal(decodeURIComponent(encodedName), outgoingName, 'Server sends the full UTF-8 filename');
    const [download] = await Promise.all([page.waitForEvent('download'), downloadLink.click()]);
    const suggestedName = download.suggestedFilename();
    assert.equal(suggestedName, outgoingName, 'Download action preserves the original Unicode filename');
    assert.equal(await download.failure(), null);
    assert.deepEqual(await fs.readFile(await download.path()), outgoing);
    assert.deepEqual(errors, []);
    if(process.env.LAN_DROP_SCREENSHOT_DIR) {
      const screenshotDirectory = path.resolve(process.env.LAN_DROP_SCREENSHOT_DIR);
      await fs.mkdir(screenshotDirectory, {recursive:true});
      await page.screenshot({path:path.join(screenshotDirectory, `lan-drop-${profileName}.jpg`), fullPage:true});
      await page.screenshot({path:path.join(screenshotDirectory, `lan-drop-${profileName}-viewport.png`)});
    }
    const session = await page.evaluate(() => sessionStorage.getItem('landrop-session'));
    const unsupported = await browser.newContext({...devices[profile.device], acceptDownloads:true});
    await unsupported.addInitScript(token => {
      globalThis.Worker = undefined;
      globalThis.AbortController = undefined;
      sessionStorage.setItem('landrop-session', token);
    }, session);
    const unsupportedPage = await unsupported.newPage();
    await unsupportedPage.goto(browserBase);
    await unsupportedPage.locator('#transfer').waitFor({state:'visible'});
    assert.equal(await unsupportedPage.locator('#file-input').isDisabled(), true);
    assert.equal(await unsupportedPage.locator('#drop').getAttribute('aria-disabled'), 'true');
    assert.equal(await unsupportedPage.locator('#capability-notice').isVisible(), true);
    // An injected change event must still be denied by the send handler.
    await unsupportedPage.locator('#file-input').setInputFiles({name:'unsupported.txt', mimeType:'text/plain', buffer:Buffer.from('blocked')});
    await delay(200);
    assert.equal((await api('/api/admin')).offers.length, 0);
    assert.equal((await api('/api/admin')).batches.length, 0);
    const [limitedDownload] = await Promise.all([unsupportedPage.waitForEvent('download'), unsupportedPage.locator('#files a').click()]);
    assert.deepEqual(await fs.readFile(await limitedDownload.path()), outgoing, 'Downloads work without upload capabilities');
    await unsupported.close();
    // Revocation invalidates both header requests and the already issued download cookie.
    await api(`/api/admin/devices/${device.id}/revoke`, 'POST');
    const deniedState = await page.evaluate(async () => (await fetch('/api/state', {headers:{'X-LanDrop-Session':sessionStorage.getItem('landrop-session')}})).status);
    assert.equal(deniedState, 401);
    const deniedDownload = await page.evaluate(async () => (await fetch(document.querySelector('#files a').href)).status);
    assert.equal(deniedDownload, 401);
    await context.close();
    console.log(`PASS: ${profileName} (${profile.engine.name()}, ${profile.device} emulation, ${tlsCertificate ? 'trusted HTTPS' : 'HTTP preview'}): EN/CS, portrait/landscape, session reload, approval gate, real hash worker, 20-file consent, saved SHA-256, cancel/clear/reject, offline second-file retry without duplicates, empty/12 MiB binary, exact download bytes/Unicode filename, capability gating and revocation. Physical devices/Safari not tested.`);
  } finally {
    await browser?.close();
    if(!exited && adminKey) {
      await fetch(base+'/api/admin/shutdown', {method:'POST', headers:{'X-LanDrop-Admin':adminKey}}).catch(()=>{});
      await until(() => exited, 'graceful server shutdown').catch(()=>{});
    }
    if(!exited) { server.kill(); await until(() => exited, 'server shutdown'); }
    const cleanupTarget = path.resolve(temporary);
    assert.equal(path.dirname(cleanupTarget), path.resolve(os.tmpdir()), 'Cleanup must stay within OS temporary directory');
    assert.equal(path.basename(cleanupTarget).startsWith('landrop-mobile-'), true, 'Cleanup must target this test directory');
    await fs.rm(cleanupTarget, {recursive:true, force:true});
  }
}
const selected = process.argv.slice(2);
if(selected.some(name=>!profiles[name])) throw new Error('Usage: node tests/mobile_browser.cjs [android] [iphone]');
(async () => { for(const name of selected.length ? selected : Object.keys(profiles)) await main(name); })()
  .catch(error => { console.error(error); process.exitCode = 1; });
