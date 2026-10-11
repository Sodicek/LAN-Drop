const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const context = {DOMException, setTimeout, clearTimeout};
vm.createContext(context);
vm.runInContext(fs.readFileSync(path.join(__dirname, '../src/LanDrop/wwwroot/offers.js'), 'utf8') + '\nglobalThis.Client=ReceiveOfferClient;globalThis.BatchClient=BatchOfferClient;', context);
const Client = context.Client;
const file = {name:'photo.jpg', size:123};

async function run() {
  const requests = [];
  let polls = 0, waiting = 0;
  const approved = new Client(async (url, options = {}) => {
    requests.push({url, ...options});
    if (options.method === 'POST') return {id:'owned offer', status:'pending'};
    return {status:++polls === 1 ? 'pending' : 'approved'};
  }, async () => {});
  assert.equal(await approved.approve(file, new AbortController().signal, () => waiting++), 'owned offer');
  assert.equal(waiting, 1);
  assert.equal(polls, 2);
  assert.deepEqual(JSON.parse(requests[0].body), {name:file.name, size:file.size});
  assert.equal(requests[1].url, '/api/offers/owned%20offer');
  assert.equal(requests.some(item => item.method === 'DELETE'), false);
  assert.equal(requests.some(item => item.url.includes('/api/files')), false, 'Approval sends metadata only');

  for (const [status, code] of [['rejected','offer_rejected'], ['expired','offer_expired'], ['consumed','offer_not_approved']]) {
    const calls = [];
    const client = new Client(async (url, options = {}) => {
      calls.push(options.method || 'GET');
      return options.method === 'POST' ? {id:'terminal', status:'pending'} : {status};
    }, async () => {});
    await assert.rejects(client.approve(file, new AbortController().signal, () => {}), error => error.code === code);
    assert.deepEqual(calls, ['POST', 'GET', 'DELETE']);
  }

  const before = new AbortController(); before.abort();
  let calls = 0;
  const blocked = new Client(async () => { calls++; });
  await assert.rejects(blocked.approve(file, before.signal, () => {}), error => error.name === 'AbortError');
  assert.equal(calls, 0, 'Already canceled selection never creates an offer');

  const during = new AbortController();
  const canceledCalls = [];
  const canceled = new Client(async (url, options = {}) => {
    canceledCalls.push(options.method || 'GET');
    return {id:'canceled', status:'pending'};
  }, async () => { during.abort(); });
  await assert.rejects(canceled.approve(file, during.signal, () => {}), error => error.name === 'AbortError');
  assert.equal(canceledCalls.at(-1), 'DELETE', 'Waiting cancellation releases the server offer');

  const race = new AbortController();
  const raceCalls = [];
  const raced = new Client(async (url, options = {}) => {
    raceCalls.push(options.method || 'GET');
    if(options.method === 'POST') return {id:'race', status:'pending'};
    if(!options.method) { race.abort(); return {status:'approved'}; }
  }, async () => {});
  await assert.rejects(raced.approve(file, race.signal, () => {}), error => error.name === 'AbortError');
  assert.equal(raceCalls.at(-1), 'DELETE', 'Approval arriving after cancellation cannot start a transfer');

  const failed = new Client(async (url, options = {}) => {
    if(options.method === 'POST') return {id:'offline', status:'pending'};
    throw new Error('Connection lost');
  }, async () => {});
  await assert.rejects(failed.approve(file, new AbortController().signal, () => {}), /Connection lost/);

  const timer = new AbortController();
  const delay = Client.delay(10000, timer.signal);
  timer.abort();
  await assert.rejects(delay, error => error.name === 'AbortError');
  await assert.rejects(Client.delay(10000, before.signal), error => error.name === 'AbortError');
  await Client.delay(1, new AbortController().signal);
  const selection = [file,{name:'second.jpg',size:456}];
  const batchCalls = [];
  const batchClient = new context.BatchClient(async(url,options={}) => {
    batchCalls.push({url,...options});
    return options.method === 'POST' ? {id:'batch id',status:'pending',offers:selection.map((f,i)=>({id:String(i),...f}))} : {status:'approved'};
  }, async()=>{});
  const acceptedBatch = await batchClient.approve(selection,new AbortController().signal,()=>{});
  assert.equal(acceptedBatch.offers.length,2);
  assert.deepEqual(JSON.parse(batchCalls[0].body),{files:selection});
  assert.equal(batchCalls[1].url,'/api/offer-batches/batch%20id');
  assert.equal(batchCalls.length,2,'One metadata request authorizes the entire selection');
  for(const status of ['rejected','expired','consumed']) {
    const calls=[];
    const client = new context.BatchClient(async(url,options={})=>{
      calls.push(options.method||'GET');
      return options.method==='POST'?{id:'batch',status:'pending'}:{status};
    },async()=>{});
    await assert.rejects(client.approve(selection,new AbortController().signal,()=>{}));
    assert.deepEqual(calls,['POST','GET','DELETE']);
  }
  const batchAbort = new AbortController(), abortedCalls=[];
  const abortedBatch = new context.BatchClient(async(url,options={})=>{
    abortedCalls.push(options.method||'GET');
    if(options.method==='POST') return {id:'aborted',status:'pending'};
    batchAbort.abort();return {status:'approved'};
  },async()=>{});
  await assert.rejects(abortedBatch.approve(selection,batchAbort.signal,()=>{}),error=>error.name==='AbortError');
  assert.equal(abortedCalls.at(-1),'DELETE','Late batch approval cannot override cancellation');
  let mismatchCleanup=false;
  const mismatchedBatch = new context.BatchClient(async(url,options={})=>{
    if(options.method==='DELETE') { mismatchCleanup=true;return; }
    return {id:'mismatch',status:'approved',offers:[{id:'1',name:'other.jpg',size:123}]};
  },async()=>{});
  await assert.rejects(mismatchedBatch.approve(selection,new AbortController().signal,()=>{}),/offer_mismatch/);
  assert.equal(mismatchCleanup,true,'Mismatched consent is canceled before any file body');
  console.log('PASS: metadata-only offers, approval polling, rejection/expiry, cancellation races, offline cleanup, abortable timers.');
}
run().catch(error => { console.error(error); process.exitCode = 1; });
