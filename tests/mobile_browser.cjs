// Chromium mobile emulation checks real browser APIs and server transfers.
// This is not a physical Android device, Android WebView, or Safari test.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const {spawn} = require('node:child_process');
const {createHash} = require('node:crypto');
const {chromium, devices} = require('@playwright/test');
const root = path.resolve(__dirname, '..');
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function until(check, label) {
  const deadline = Date.now() + 20000;
  while(Date.now() < deadline) { const value = await check(); if(value) return value; await delay(100); }
  throw new Error('Timed out: ' + label);
}
async function main() {
  const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'landrop-mobile-'));
  const listener = net.createServer();
  await new Promise(resolve => listener.listen(0, '127.0.0.1', resolve));
  const port = listener.address().port;
  await new Promise(resolve => listener.close(resolve));
  const base = `http://127.0.0.1:${port}`;
  let output = '', browser;
  const server = spawn('dotnet', [path.join(root, 'src/LanDrop/bin/Release/net10.0/LanDrop.dll'),
    `--Port=${port}`, '--OpenBrowser=false', '--ListenAddress=127.0.0.1', `--DataDirectory=${temporary}`],
    {cwd:path.join(root, 'src/LanDrop'), windowsHide:true, stdio:['ignore','pipe','pipe']});
  server.stdout.on('data', chunk => output += chunk);
  server.stderr.on('data', chunk => output += chunk);
  let exited = false; server.on('exit', () => exited = true);
  try {
    const adminKey = await until(() => {
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
    const qr = await api('/api/admin/qr?address=127.0.0.1');
    browser = await chromium.launch();
    const context = await browser.newContext({...devices['Pixel 7'], locale:'en-US', acceptDownloads:true});
    const page = await context.newPage();
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.goto(qr.link);
    assert.equal(await page.locator('#platform').inputValue(), 'auto');
    assert.equal(await page.locator('#device-name').inputValue(), 'My Android');
    await page.locator('#language').selectOption('cs');
    assert.equal(await page.locator('#device-name').inputValue(), 'Můj Android');
    await page.locator('#device-name').fill('Pixel – QA');
    await page.locator('#platform').selectOption('ios');
    assert.equal(await page.locator('#device-name').inputValue(), 'Pixel – QA');
    await page.locator('#platform').selectOption('android');
    await page.locator('#language').selectOption('en');
    await page.locator('#pair-form button').click();
    const device = await until(async () => (await api('/api/admin')).devices.find(d => d.name === 'Pixel – QA'), 'Android pairing');
    await api(`/api/admin/devices/${device.id}/approve`, 'POST');
    await page.locator('#transfer').waitFor({state:'visible'});
    assert.equal(await page.locator('#file-input').isEnabled(), true);
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, 'Mobile page fits viewport');
    const payload = Buffer.from('Android browser upload\nŽluťoučký kůň 🐎\n'.repeat(2000));
    const name = 'Android – příloha 🐎.txt';
    await page.locator('#file-input').setInputFiles({name, mimeType:'text/plain', buffer:payload});
    const offer = await until(async () => (await api('/api/admin')).offers.find(o => o.name === name), 'browser hash worker and offer');
    assert.equal(offer.size, payload.length);
    assert.equal((await fs.readdir(inbox)).length, 0, 'Nothing is saved before PC accepts');
    await api(`/api/admin/offers/${offer.id}/approve`, 'POST');
    await page.locator('#history .row').first().waitFor();
    assert.deepEqual(await fs.readFile(path.join(inbox, name)), payload);
    const received = (await api('/api/admin')).files.find(f => f.name === name);
    assert.equal(received.sha256, createHash('sha256').update(payload).digest('hex'));
    assert.equal(received.verified, true);
    // Cancelling a second offer must not write a file or consume PC storage.
    await page.locator('#file-input').setInputFiles({name:'cancelled-android.txt', mimeType:'text/plain', buffer:Buffer.from('do not save')});
    await until(async () => (await api('/api/admin')).offers.find(o => o.name === 'cancelled-android.txt'), 'cancel offer');
    await page.locator('#cancel').click();
    await until(async () => !(await api('/api/admin')).offers.some(o => o.name === 'cancelled-android.txt'), 'offer cancelled');
    assert.deepEqual(await fs.readdir(inbox), [name]);
    // Outbound download uses the phone's cookie and preserves UTF-8 filename/bytes.
    const outgoingName = 'PC – fotografie č. 1.bin';
    const outgoing = Buffer.from([0,1,2,255,128,64,0,9]);
    const prepared = await fetch(base + `/api/files?name=${encodeURIComponent(outgoingName)}&recipient=${device.id}`, {
      method:'PUT', headers:{...admin, 'Content-Type':'application/octet-stream', 'X-LanDrop-SHA256':createHash('sha256').update(outgoing).digest('hex')}, body:outgoing});
    assert.equal(prepared.status, 200, await prepared.text());
    const downloadLink = page.locator('#files a').filter({hasText:'Download'});
    await downloadLink.waitFor();
    const [download] = await Promise.all([page.waitForEvent('download'), downloadLink.click()]);
    assert.equal(download.suggestedFilename(), outgoingName);
    assert.equal(await download.failure(), null);
    assert.deepEqual(await fs.readFile(await download.path()), outgoing);
    assert.deepEqual(errors, []);
    if(process.env.LAN_DROP_SCREENSHOT_DIR) {
      const screenshotDirectory = path.resolve(process.env.LAN_DROP_SCREENSHOT_DIR);
      await fs.mkdir(screenshotDirectory, {recursive:true});
      await page.screenshot({path:path.join(screenshotDirectory, 'lan-drop-android.jpg'), fullPage:true});
    }
    const session = await page.evaluate(() => sessionStorage.getItem('landrop-session'));
    const unsupported = await browser.newContext({...devices['Pixel 7'], acceptDownloads:true});
    await unsupported.addInitScript(token => {
      globalThis.Worker = undefined;
      globalThis.AbortController = undefined;
      sessionStorage.setItem('landrop-session', token);
    }, session);
    const unsupportedPage = await unsupported.newPage();
    await unsupportedPage.goto(base);
    await unsupportedPage.locator('#transfer').waitFor({state:'visible'});
    assert.equal(await unsupportedPage.locator('#file-input').isDisabled(), true);
    assert.equal(await unsupportedPage.locator('#drop').getAttribute('aria-disabled'), 'true');
    assert.equal(await unsupportedPage.locator('#capability-notice').isVisible(), true);
    // An injected change event must still be denied by the send handler.
    await unsupportedPage.locator('#file-input').setInputFiles({name:'unsupported.txt', mimeType:'text/plain', buffer:Buffer.from('blocked')});
    await delay(200);
    assert.equal((await api('/api/admin')).offers.length, 0);
    const [limitedDownload] = await Promise.all([unsupportedPage.waitForEvent('download'), unsupportedPage.locator('#files a').click()]);
    assert.deepEqual(await fs.readFile(await limitedDownload.path()), outgoing, 'Downloads work without upload capabilities');
    await unsupported.close();
    await context.close();
    // iPhone user-agent remains supported; Chromium does not represent Safari.
    await api('/api/admin/pairing', 'POST');
    const iphoneQr = await api('/api/admin/qr?address=127.0.0.1');
    const iphone = await browser.newContext({...devices['iPhone 13']});
    const iphonePage = await iphone.newPage();
    await iphonePage.goto(iphoneQr.link);
    assert.equal(await iphonePage.locator('#device-name').inputValue(), 'My iPhone');
    await iphonePage.locator('#platform').selectOption('android');
    assert.equal(await iphonePage.locator('#device-name').inputValue(), 'My Android');
    await iphone.close();
    console.log('PASS: Chromium Android emulation: EN/CS defaults, overrides/custom name, mobile layout, pairing, actual hash-worker upload/approval/cancel, saved bytes/SHA-256, cookie Unicode download; missing Worker/AbortController blocks uploads but permits downloads; iPhone UA defaults. Physical devices/Safari not tested.');
  } finally {
    await browser?.close();
    if(!exited) { server.kill(); await until(() => exited, 'server shutdown'); }
    const cleanupTarget = path.resolve(temporary);
    assert.equal(path.dirname(cleanupTarget), path.resolve(os.tmpdir()), 'Cleanup must stay within OS temporary directory');
    assert.equal(path.basename(cleanupTarget).startsWith('landrop-mobile-'), true, 'Cleanup must target this test directory');
    await fs.rm(cleanupTarget, {recursive:true, force:true});
  }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
