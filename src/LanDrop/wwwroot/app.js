const $ = id => document.getElementById(id);
const fragment = new URLSearchParams(location.hash.slice(1));
if (fragment.has('admin')) sessionStorage.setItem('landrop-admin', fragment.get('admin'));
let admin = sessionStorage.getItem('landrop-admin');
let token = sessionStorage.getItem('landrop-session');
let pairCode = fragment.get('pair');
history.replaceState(null, '', location.pathname);
let busy = false, cancelled = false, current = null, offerCancellation = null, qrUrl = null, qrAddress = null, lastState = null;
let platformPreference = 'auto', customDeviceName = false;
let transferQueue = null, lastTransferEvents = [];
try { platformPreference = MobileUX.normalizePreference(localStorage.getItem('landrop-platform')); } catch {}
const uploadSupport = MobileUX.uploadCapabilities(globalThis);
function applyMobileUX() {
  const platform = MobileUX.resolvePlatform(platformPreference, navigator);
  $('platform').value = platformPreference;
  $('mobile-help').hidden = Boolean(admin);
  $('browser-help').textContent = t(platform === 'android' ? 'androidBrowserHelp' : platform === 'ios' ? 'iosBrowserHelp' : 'otherBrowserHelp');
  $('download-help').hidden = Boolean(admin);
  $('download-help').textContent = t(platform === 'android' ? 'androidDownloadHelp' : platform === 'ios' ? 'iosDownloadHelp' : 'otherDownloadHelp');
  if(!customDeviceName && MobileUX.isDefaultDeviceName($('device-name').value)) $('device-name').value = MobileUX.defaultDeviceName(platform, locale());
  $('capability-notice').hidden = uploadSupport.supported;
  $('capability-notice').textContent = t('uploadUnsupported');
  $('file-input').disabled = busy || !uploadSupport.supported;
  $('drop').setAttribute('aria-disabled', String(!uploadSupport.supported));
  $('drop').classList.toggle('disabled', !uploadSupport.supported);
  if(admin && !uploadSupport.supported) { $('capability-notice').hidden = false; $('transfer').prepend($('capability-notice')); }
}
const headers = () => admin ? {'X-LanDrop-Admin': admin} : {'X-LanDrop-Session': token || ''};
function notice(message, error = false) { $('notice').textContent = message; $('notice').classList.toggle('error', error); }
async function api(path, options = {}) {
  if(typeof fetch !== 'function') throw new Error(t('browserUnsupported'));
  const deadline = typeof AbortController === 'function' ? new AbortController() : null;
  const cancel = () => deadline?.abort();
  options.signal?.addEventListener('abort', cancel, {once:true});
  if(options.signal?.aborted) cancel();
  const timer = setTimeout(cancel, 15000);
  try {
    const response = await fetch(path, {...options, signal:deadline?.signal, headers: {...headers(), ...options.headers}});
    if (!response.ok) { let body; try { body = await response.json(); } catch {} throw new Error(errorMessage(body, response.status)); }
    const text = await response.text(); return text ? JSON.parse(text) : null;
  } catch(error) {
    if(error.name === 'AbortError' && !options.signal?.aborted) throw new Error(t('networkError'));
    throw error;
  } finally { clearTimeout(timer); options.signal?.removeEventListener('abort', cancel); }
}
const post = (path, data) => api(path, {method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify(data || {})});
function size(bytes) { const units = ['B','KB','MB','GB']; let i = 0; while(bytes >= 1024 && i < 3) { bytes /= 1024; i++; } return bytes.toLocaleString(locale(), {minimumFractionDigits:i ? 1 : 0, maximumFractionDigits:i ? 1 : 0}) + ' ' + units[i]; }
function row(name, detail) { const el = document.createElement('div'); el.className = 'row'; const info = document.createElement('div'); info.className = 'info'; const title = document.createElement('strong'); title.textContent = name; const small = document.createElement('small'); small.textContent = detail; info.append(title, small); el.append(info); return el; }
function button(label, action) { const b = document.createElement('button'); b.className = 'secondary'; b.textContent = label; b.onclick = async () => { b.disabled = true; try { await action(); await refresh(); } catch(e) { notice(e.message,true); } finally { b.disabled = false; } }; return b; }
async function qr() {
  if (!$('address').value) { $('qr').textContent = t('noAddress'); return; }
  const result = await api('/api/admin/qr?address=' + encodeURIComponent($('address').value));
  if(qrUrl) URL.revokeObjectURL(qrUrl);
  qrUrl = URL.createObjectURL(new Blob([result.svg], {type:'image/svg+xml'}));
  const img = document.createElement('img'); img.src = qrUrl; img.alt = t('qrAlt'); $('qr').replaceChildren(img);
  $('pair-link').href = result.link; qrAddress = $('address').value;
}
async function refresh(cachedState = null) {
  if (admin) {
    const data = cachedState || await api('/api/admin');
    lastState = data;
    $('desktop').hidden = false; $('transfer').hidden = false; $('file-section').hidden = false;
    $('transfer-title').textContent = t('prepare'); $('files-title').textContent = t('transfers');
    if (document.activeElement !== $('folder') && !$('folder').dataset.edited) $('folder').value = data.inbox;
    const oldAddress = $('address').value;
    const addresses = [data.listeningAddress].filter(Boolean);
    const loopback = !data.listeningAddress || data.listeningAddress === '127.0.0.1' || data.listeningAddress === '::1';
    $('listen-note').hidden = !loopback;
    if (JSON.stringify([...$('address').options].map(o=>o.value)) !== JSON.stringify(addresses)) {
      $('address').replaceChildren(...addresses.map(a=>new Option(a,a)));
      if (addresses.includes(oldAddress)) $('address').value = oldAddress;
    }
    if(loopback) { $('qr').replaceChildren(); $('pair-link').hidden = true; }
    else { $('pair-link').hidden = false; if (qrAddress !== $('address').value) await qr(); }
    $('expiry').textContent = Date.now() < Date.parse(data.pairingExpires) ? t('qrUntil', {time:new Date(data.pairingExpires).toLocaleTimeString(locale())}) : t('qrExpired');
    $('devices').replaceChildren();
    if (!data.devices.length) $('devices').textContent = t('noDevices');
    for (const d of data.devices) { const el = row(d.name, d.approved ? t('approved') : t('pending')); if (!d.approved) el.append(button(t('approve'),()=>post('/api/admin/devices/'+d.id+'/approve'))); el.append(button(d.approved ? t('disconnect') : t('reject'),()=>post('/api/admin/devices/'+d.id+'/revoke'))); $('devices').append(el); }
    $('recipient-control').hidden = false;
    const selectedRecipient = $('recipient').value;
    const approvedDevices = data.devices.filter(d=>d.approved);
    $('recipient').replaceChildren(new Option(t('noRecipient'), ''), ...approvedDevices.map(d=>new Option(d.name,d.id)));
    if(approvedDevices.some(d=>d.id === selectedRecipient)) $('recipient').value = selectedRecipient;
    else if(approvedDevices.length === 1 && !transferQueue?.remaining().length) $('recipient').value = approvedDevices[0].id;
    renderFiles(data.files, true);
    $('offer-section').hidden = false;
    $('offers').replaceChildren();
    if(!data.offers?.length) $('offers').textContent = t('noOffers');
    for(const offer of data.offers || []) {
      const el = row(offer.name, offer.deviceName + ' · ' + size(offer.size));
      el.append(button(t('acceptFile'),()=>post('/api/admin/offers/'+offer.id+'/approve')),
        button(t('reject'),()=>post('/api/admin/offers/'+offer.id+'/reject')));
      $('offers').append(el);
    }
    $('batch-section').hidden = false;
    $('batches').replaceChildren();
    if(!data.batches?.length) $('batches').textContent = t('noBatches');
    for(const batch of data.batches || []) {
      const el = row(batch.deviceName, t('batchSummary',{count:batch.files.length,size:size(batch.totalSize)}));
      const details = document.createElement('details');
      const summary = document.createElement('summary'); summary.textContent = t('viewFiles'); details.append(summary);
      for(const file of batch.files) { const name = document.createElement('p'); name.textContent = file.name+' · '+size(file.size); details.append(name); }
      el.querySelector('.info').append(details);
      el.append(button(t('acceptBatch'),()=>post('/api/admin/offer-batches/'+batch.id+'/approve')),
        button(t('reject'),()=>post('/api/admin/offer-batches/'+batch.id+'/reject')));
      $('batches').append(el);
    }
    renderTransfers(data.transfers || []);
  } else if (token) {
    const data = cachedState || await api('/api/state');
    lastState = data;
    $('waiting').hidden = data.approved; $('transfer').hidden = !data.approved; $('file-section').hidden = !data.approved;
    if (data.approved) { if (!cachedState) await post('/api/download-session'); renderFiles(data.files, false); renderTransfers(cachedState ? lastTransferEvents : await api('/api/transfers')); }
  }
}
function renderTransfers(events) {
  lastTransferEvents = events;
  $('transfer-overview').hidden = false; $('transfer-events').replaceChildren();
  if(!events.length) $('transfer-events').textContent = t('noTransfers');
  for(const event of [...events].sort((a,b)=>Date.parse(b.updated)-Date.parse(a.updated))) {
    const el = row(event.name, (event.deviceName ? event.deviceName+' · ' : '')+t('state_'+event.status)+' · '+size(event.bytesTransferred)+' / '+size(event.size));
    if(event.errorCode) { const message = document.createElement('small'); message.textContent = t(event.errorCode); el.querySelector('.info').append(message); }
    $('transfer-events').append(el);
  }
}
function renderQueue() {
  $('queue-list').replaceChildren();
  for(const item of transferQueue?.items || []) {
    const el = row(item.file.name, t('state_'+item.state)+(item.error ? ' · '+item.error : ''));
    el.dataset.state = item.state; $('queue-list').append(el);
  }
  $('retry-area').hidden = busy || !transferQueue?.remaining().length;
  $('retry').disabled = busy || !uploadSupport.supported;
  $('clear-queue').disabled = busy;
}
function renderFiles(files, local) {
  $('files').replaceChildren();
  if (!files.length) $('files').textContent = local ? t('noTransfers') : t('noFiles');
  for (const f of files) { const checked = f.verified === true && /^[a-f0-9]{64}$/.test(f.sha256); const el = row(f.name, size(f.size) + (local ? (f.direction === 'outbound' ? t('preparedSuffix') : t('receivedSuffix')) : '') + (checked ? t('pcCopyChecked') : '')); if (local) el.append(button(f.direction === 'outbound' ? t('unshare') : t('hide'),()=>api('/api/admin/files/'+f.id,{method:'DELETE'}))); else { const a = document.createElement('a'); a.href = '/download/'+f.id; a.textContent = t('download'); a.setAttribute('download',f.name); el.append(a); } $('files').append(el); }
}
function upload(file, index, count, recipientId, offerId, sha256, item) {
  return new Promise((resolve,reject) => {
    const meter = new TransferMeter(file.size);
    let lastRender = -Infinity;
    const renderProgress = (loaded, force = false) => {
      const now = performance.now();
      if(!force && now - lastRender < 250 && loaded < file.size) return;
      lastRender = now;
      const sample = meter.sample(loaded);
      $('progress').value = sample.percent;
      const eta = sample.remainingSeconds === null ? t('estimating') :
        sample.remainingSeconds >= 3600 ? t('etaHours', {value:Math.ceil(sample.remainingSeconds/3600)}) :
        sample.remainingSeconds >= 60 ? t('etaMinutes', {value:Math.ceil(sample.remainingSeconds/60)}) :
        t('etaSeconds', {value:sample.remainingSeconds});
      const detail = loaded >= file.size ? t('waitingForSave') : t('speedEta', {speed:size(sample.speed), eta});
      $('progress-text').textContent = index+'/'+count+' · '+file.name+' · '+Math.round(sample.percent)+' % · '+detail;
    };
    const xhr = current = new XMLHttpRequest();
    xhr.open('PUT','/api/files?name='+encodeURIComponent(file.name)+(admin ? '&recipient='+encodeURIComponent(recipientId) : '&offer='+encodeURIComponent(offerId)));
    for(const [k,v] of Object.entries(headers())) xhr.setRequestHeader(k,v);
    xhr.setRequestHeader('Content-Type','application/octet-stream');
    xhr.setRequestHeader('X-LanDrop-SHA256',sha256);
    xhr.upload.onprogress = e => { if(e.lengthComputable) renderProgress(e.loaded); };
    xhr.upload.onload = () => { transferQueue.transition(item,'verifying'); renderQueue(); renderProgress(file.size, true); };
    xhr.onload = () => {
      let body; try { body = JSON.parse(xhr.responseText); } catch {}
      if(xhr.status >= 200 && xhr.status < 300) {
        if(validUploadReceipt(body, file.size, sha256)) resolve();
        else reject(new Error(t('receipt_invalid')));
      } else reject(new Error(errorMessage(body, xhr.status)));
    };
    xhr.onerror = () => reject(new Error(t('networkError')));
    xhr.onabort = () => reject(new Error(t('cancelled')));
    renderProgress(0, true);
    xhr.send(file);
  });
}
async function send(files, retry = false) {
  if(!uploadSupport.supported) { notice(t('uploadUnsupported'), true); return; }
  if(busy || !files.length) return;
  const recipientId = admin ? $('recipient').value : null;
  if(admin && !recipientId) { notice(t('noRecipient'), true); return; }
  if(files.length > 1000 || files.some(file => file.size > 10*1024**3)) { notice(t('invalid_batch'),true); return; }
  if(!retry) transferQueue = new TransferQueue(files);
  const pending = transferQueue.remaining();
  $('recipient').disabled = true;
  busy = true; cancelled = false; $('file-input').disabled = true; $('progress-area').hidden = false; renderQueue();
  let batch, activeItem;
  try {
    offerCancellation = new AbortController();
    if(!admin) {
      for(const item of pending) transferQueue.transition(item,'waiting'); renderQueue();
      batch = await new BatchOfferClient(api).approve(pending.map(item=>item.file), offerCancellation.signal, () => {
        $('progress').value = 0; $('progress-text').textContent = t('waitingForBatchApproval',{count:pending.length});
      });
    }
    for(let i=0; i<pending.length; i++) {
      if(cancelled) throw new DOMException('Cancelled','AbortError');
      activeItem = pending[i]; const file = activeItem.file;
      transferQueue.transition(activeItem,'preparing'); renderQueue();
      const sha256 = await hashFile(file, offerCancellation.signal, (loaded,total) => {
        const percent = total === 0 ? 100 : Math.round(loaded*100/total);
        $('progress').value = percent; $('progress-text').textContent = (i+1)+'/'+pending.length+' · '+file.name+' · '+t('checkingFile',{percent});
      });
      if(cancelled) throw new DOMException('Cancelled','AbortError');
      transferQueue.transition(activeItem,'transferring'); renderQueue();
      await upload(file,i+1,pending.length,recipientId,batch?.offers[i].id,sha256,activeItem);
      transferQueue.transition(activeItem,'completed'); renderQueue();
      $('history').prepend(row(file.name, admin ? t('prepared') : t('saved')));
      activeItem = null;
    }
    if(!cancelled) notice(t(admin ? 'outgoingDone' : 'done'));
    await refresh();
  } catch(e) {
    const message = cancelled || e.name === 'AbortError' ? t('cancelled') : e.code ? t(e.code) : translations.en[e.message] ? t(e.message) : e.message;
    if(activeItem) transferQueue.transition(activeItem,cancelled ? 'cancelled' : 'failed',message);
    else for(const item of pending) if(item.state !== 'completed') transferQueue.transition(item,cancelled ? 'cancelled' : 'failed',message);
    for(const item of pending) if(item.state === 'waiting') transferQueue.transition(item,'queued');
    notice(message,true);
  }
  finally {
    if(batch?.id) await api('/api/offer-batches/'+encodeURIComponent(batch.id),{method:'DELETE'}).catch(()=>{});
    offerCancellation = null; current = null; busy = false; $('recipient').disabled = false; $('file-input').disabled = !uploadSupport.supported; $('file-input').value = ''; $('progress-area').hidden = true; renderQueue();
  }
}
$('retry').onclick = () => { if(transferQueue && !busy) send(transferQueue.remaining().map(item=>item.file),true); };
$('clear-queue').onclick = () => { if(!busy) { transferQueue = null; renderQueue(); } };
$('file-input').onchange = e => send([...e.target.files]);
$('cancel').onclick = () => { cancelled = true; offerCancellation?.abort(); current?.abort(); };
$('drop').ondragover = e => { e.preventDefault(); if(uploadSupport.supported) $('drop').classList.add('drag'); };
$('drop').ondragleave = () => $('drop').classList.remove('drag');
$('drop').ondrop = e => { e.preventDefault(); $('drop').classList.remove('drag'); if(uploadSupport.supported) send([...e.dataTransfer.files]); };
$('address').onchange = () => qr().catch(e=>notice(e.message,true));
$('renew').onclick = async () => { try { await post('/api/admin/pairing'); await qr(); await refresh(); notice(t('qrReady')); } catch(e) { notice(e.message,true); } };
$('folder').oninput = () => $('folder').dataset.edited = 'true';
$('folder-form').onsubmit = async e => { e.preventDefault(); try { await post('/api/admin/folder',{path:$('folder').value}); delete $('folder').dataset.edited; await refresh(); notice(t('folderChanged')); } catch(e) { notice(e.message,true); } };
$('pair-form').onsubmit = async e => { e.preventDefault(); const b = e.target.querySelector('button'); b.disabled = true; try { const r = await post('/api/pair',{code:pairCode,name:$('device-name').value}); token = r.token; sessionStorage.setItem('landrop-session',token); $('pair').hidden = true; notice(t('confirmPc')); await refresh(); } catch(e) { notice(e.message,true); } finally { b.disabled = false; } };
applyLanguage();
applyMobileUX();
$('device-name').oninput = () => { customDeviceName = true; };
$('platform').onchange = () => { platformPreference = MobileUX.normalizePreference($('platform').value); try { localStorage.setItem('landrop-platform', platformPreference); } catch {} applyMobileUX(); };
$('language').onchange = () => { setLanguage($('language').value); applyMobileUX(); renderQueue(); refresh(lastState).catch(e=>notice(e.message,true)); const qrImage = $('qr').querySelector('img'); if(qrImage) qrImage.alt = t('qrAlt'); };
if (pairCode && !admin) { token = null; sessionStorage.removeItem('landrop-session'); $('pair').hidden = false; }
else if(!admin && !token) $('welcome').hidden = false;
else refresh().catch(e=>notice(e.message,true));
if(typeof fetch !== 'function') notice(t('browserUnsupported'), true);
else setInterval(()=>{ if(admin || token) refresh().catch(e=>notice(e.message,true)); },3000);
