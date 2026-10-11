const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const web = path.join(__dirname, '../src/LanDrop/wwwroot');
const expectedHash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');

async function checkWorker(bytes) {
  const messages = [], slices = [];
  const context = {performance, self:{postMessage:value => messages.push(value)}};
  vm.createContext(context);
  context.importScripts = url => {
    assert.equal(url, '/vendor/sha256.js');
    vm.runInContext(fs.readFileSync(path.join(web,'vendor/sha256.js'),'utf8'), context);
  };
  vm.runInContext(fs.readFileSync(path.join(web,'hash-worker.js'),'utf8'), context);
  const file = {size:bytes.length, slice(start,end) {
    slices.push([start,end]);
    assert(end-start <= 1024*1024, 'Hashing must use bounded chunks');
    return {async arrayBuffer() {
      const part = bytes.subarray(start,end);
      return part.buffer.slice(part.byteOffset,part.byteOffset+part.byteLength);
    }};
  }};
  await context.self.onmessage({data:file});
  assert.equal(messages.at(-1).sha256, expectedHash(bytes));
  assert(!messages.some(message => message.error));
  if(bytes.length) {
    const progress = messages.filter(message => message.loaded !== undefined);
    assert.equal(progress.at(-1).loaded, bytes.length);
    assert(progress.every((message,index) => message.total === bytes.length &&
      (index === 0 || message.loaded > progress[index-1].loaded)));
    assert.equal(slices[0][0],0);
    assert.equal(slices.at(-1)[1],bytes.length);
    assert(slices.every((slice,index) => index === 0 || slice[0] === slices[index-1][1]));
  }
}

(async () => {
  // Compare actual bundled code and worker against Node's independent SHA-256 implementation.
  for(const bytes of [Buffer.alloc(0),Buffer.from('abc'),Buffer.alloc(1000000,97),
    ...[1,55,56,63,64,65,119,120,127,128,129,1024*1024,1024*1024+1,3*1024*1024+73]
      .map(size => Buffer.from(Array.from({length:size},(_,index) => (index*31+7)%256)))]) {
    await checkWorker(bytes);
  }
  const context = {DOMException};
  vm.createContext(context);
  vm.runInContext(fs.readFileSync(path.join(web,'integrity.js'),'utf8')+
    '\nglobalThis.api={hashFile,validUploadReceipt};', context);
  const {hashFile,validUploadReceipt} = context.api;
  const digest = expectedHash(Buffer.from('abc'));
  function worker() {
    return {terminated:0, posted:null, terminate(){this.terminated++;},postMessage(value){this.posted=value;}};
  }
  const controller = new AbortController(), good = worker(), progress = [];
  const pending = hashFile({size:3}, controller.signal,(...value)=>progress.push(value),()=>good);
  good.onmessage({data:{loaded:3,total:3}});
  good.onmessage({data:{sha256:digest}});
  assert.equal(await pending,digest);
  assert.deepEqual(progress,[[3,3]]);
  assert.equal(good.terminated,1);
  controller.abort();
  assert.equal(good.terminated,1,'Successful hashing removes abort listener');
  for(const complete of [w=>w.onmessage({data:{error:true}}),w=>w.onerror(),
    w=>w.onmessage({data:{sha256:'broken'}})]) {
    const failed = worker();
    const task = hashFile({},new AbortController().signal,()=>{},()=>failed);
    complete(failed);
    await assert.rejects(task,/checksum_failed/);
    assert.equal(failed.terminated,1);
  }
  const cancelled = worker(), cancellation = new AbortController();
  const cancelledTask = hashFile({},cancellation.signal,()=>{},()=>cancelled);
  cancellation.abort();
  await assert.rejects(cancelledTask,error=>error.name === 'AbortError');
  assert.equal(cancelled.terminated,1);
  const alreadyCancelled = new AbortController(); alreadyCancelled.abort();
  await assert.rejects(hashFile({},alreadyCancelled.signal,()=>{},()=>{throw Error('must not create');}),
    error=>error.name === 'AbortError');
  await assert.rejects(hashFile({},new AbortController().signal,()=>{},()=>{throw Error('worker unavailable');}),/checksum_failed/);
  const receipt = {id:'id',name:'test.txt',size:3,sha256:digest,verified:true};
  assert.equal(validUploadReceipt(receipt,3,digest),true);
  for(const change of [{size:4},{size:'3'},{sha256:'0'.repeat(64)},{verified:false},{verified:'true'},{id:''},{name:''}])
    assert.equal(validUploadReceipt({...receipt,...change},3,digest),false);
  assert.equal(validUploadReceipt(null,3,digest),false);
  assert.equal(validUploadReceipt(receipt,3,digest.toUpperCase()),false);
  const emptyDigest = expectedHash(Buffer.alloc(0));
  assert.equal(validUploadReceipt({...receipt,size:0,sha256:emptyDigest},0,emptyDigest),true);
  console.log('PASS: bundled SHA-256 vectors, padding/chunk boundaries, bounded worker reads, cancellation/errors and strict saved-file receipts.');
})().catch(error=>{console.error(error);process.exitCode=1;});
