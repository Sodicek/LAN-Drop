const $ = id => document.getElementById(id);
const fragment = new URLSearchParams(location.hash.slice(1));
if (fragment.has('admin')) sessionStorage.setItem('landrop-admin', fragment.get('admin'));
let admin = sessionStorage.getItem('landrop-admin');
let token = sessionStorage.getItem('landrop-session');
let pairCode = fragment.get('pair');
history.replaceState(null, '', location.pathname);
let busy = false, cancelled = false, current = null, qrUrl = null, qrAddress = null, lastState = null;
const headers = () => admin ? {'X-LanDrop-Admin': admin} : {'X-LanDrop-Session': token || ''};
function notice(message, error = false) { $('notice').textContent = message; $('notice').classList.toggle('error', error); }
async function api(path, options = {}) {
  const response = await fetch(path, {...options, headers: {...headers(), ...options.headers}});
  if (!response.ok) { let body; try { body = await response.json(); } catch {} throw new Error(errorMessage(body, response.status)); }
  const text = await response.text(); return text ? JSON.parse(text) : null;
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
    else if(approvedDevices.length === 1) $('recipient').value = approvedDevices[0].id;
    renderFiles(data.files, true);
  } else if (token) {
    const data = cachedState || await api('/api/state');
    lastState = data;
    $('waiting').hidden = data.approved; $('transfer').hidden = !data.approved; $('file-section').hidden = !data.approved;
    if (data.approved) { if (!cachedState) await post('/api/download-session'); renderFiles(data.files, false); }
  }
}
function renderFiles(files, local) {
  $('files').replaceChildren();
  if (!files.length) $('files').textContent = local ? t('noTransfers') : t('noFiles');
  for (const f of files) { const el = row(f.name, size(f.size) + (local ? (f.direction === 'outbound' ? t('preparedSuffix') : t('receivedSuffix')) : '')); if (local) el.append(button(f.direction === 'outbound' ? t('unshare') : t('hide'),()=>api('/api/admin/files/'+f.id,{method:'DELETE'}))); else { const a = document.createElement('a'); a.href = '/download/'+f.id; a.textContent = t('download'); a.setAttribute('download',f.name); el.append(a); } $('files').append(el); }
}
function upload(file, index, count, recipientId) {
  return new Promise((resolve,reject) => {
    const xhr = current = new XMLHttpRequest();
    xhr.open('PUT','/api/files?name='+encodeURIComponent(file.name)+(admin ? '&recipient='+encodeURIComponent(recipientId) : ''));
    for(const [k,v] of Object.entries(headers())) xhr.setRequestHeader(k,v);
    xhr.setRequestHeader('Content-Type','application/octet-stream');
    xhr.upload.onprogress = e => { $('progress').value = e.lengthComputable ? e.loaded/e.total*100 : 0; $('progress-text').textContent = index+'/'+count+' · '+file.name+' · '+Math.round($('progress').value)+' %'; };
    xhr.onload = () => { if(xhr.status >= 200 && xhr.status < 300) resolve(); else { let body; try { body = JSON.parse(xhr.responseText); } catch {} reject(new Error(errorMessage(body, xhr.status))); } };
    xhr.onerror = () => reject(new Error(t('networkError')));
    xhr.onabort = () => reject(new Error(t('cancelled')));
    xhr.send(file);
  });
}
async function send(files) {
  if(busy || !files.length) return;
  const recipientId = admin ? $('recipient').value : null;
  if(admin && !recipientId) { notice(t('noRecipient'), true); return; }
  $('recipient').disabled = true;
  busy = true; cancelled = false; $('file-input').disabled = true; $('progress-area').hidden = false;
  try {
    for(let i=0; i<files.length; i++) {
      if(cancelled) break;
      if(files[i].size > 10*1024**3) throw new Error(t('invalidSize'));
      $('progress').value = 0; $('progress-text').textContent = (i+1)+'/'+files.length+' · '+files[i].name;
      await upload(files[i],i+1,files.length,recipientId);
      $('history').prepend(row(files[i].name, admin ? t('prepared') : t('saved')));
    }
    if(!cancelled) notice(t(admin ? 'outgoingDone' : 'done'));
    await refresh();
  } catch(e) { notice(e.message,true); }
  finally { current = null; busy = false; $('recipient').disabled = false; $('file-input').disabled = false; $('file-input').value = ''; $('progress-area').hidden = true; }
}
$('file-input').onchange = e => send([...e.target.files]);
$('cancel').onclick = () => { cancelled = true; current?.abort(); };
$('drop').ondragover = e => { e.preventDefault(); $('drop').classList.add('drag'); };
$('drop').ondragleave = () => $('drop').classList.remove('drag');
$('drop').ondrop = e => { e.preventDefault(); $('drop').classList.remove('drag'); send([...e.dataTransfer.files]); };
$('address').onchange = () => qr().catch(e=>notice(e.message,true));
$('renew').onclick = async () => { try { await post('/api/admin/pairing'); await qr(); await refresh(); notice(t('qrReady')); } catch(e) { notice(e.message,true); } };
$('folder').oninput = () => $('folder').dataset.edited = 'true';
$('folder-form').onsubmit = async e => { e.preventDefault(); try { await post('/api/admin/folder',{path:$('folder').value}); delete $('folder').dataset.edited; await refresh(); notice(t('folderChanged')); } catch(e) { notice(e.message,true); } };
$('pair-form').onsubmit = async e => { e.preventDefault(); const b = e.target.querySelector('button'); b.disabled = true; try { const r = await post('/api/pair',{code:pairCode,name:$('device-name').value}); token = r.token; sessionStorage.setItem('landrop-session',token); $('pair').hidden = true; notice(t('confirmPc')); await refresh(); } catch(e) { notice(e.message,true); } finally { b.disabled = false; } };
applyLanguage();
$('language').onchange = () => { setLanguage($('language').value); refresh(lastState).catch(e=>notice(e.message,true)); const qrImage = $('qr').querySelector('img'); if(qrImage) qrImage.alt = t('qrAlt'); };
if (pairCode && !admin) { token = null; sessionStorage.removeItem('landrop-session'); $('pair').hidden = false; }
else if(!admin && !token) $('welcome').hidden = false;
else refresh().catch(e=>notice(e.message,true));
setInterval(()=>{ if(admin || token) refresh().catch(e=>notice(e.message,true)); },3000);
