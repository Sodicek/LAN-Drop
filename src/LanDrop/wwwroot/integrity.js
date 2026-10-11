function hashFile(file, signal, progress, makeWorker = () => new Worker('/hash-worker.js')) {
  return new Promise((resolve, reject) => {
    if(signal.aborted) { reject(new DOMException('Cancelled', 'AbortError')); return; }
    let worker, settled = false;
    const finish = (error, hash) => {
      if(settled) return;
      settled = true;
      worker?.terminate(); signal.removeEventListener('abort', cancel);
      if(error) reject(error); else resolve(hash);
    };
    const cancel = () => finish(new DOMException('Cancelled', 'AbortError'));
    try {
      worker = makeWorker();
      signal.addEventListener('abort', cancel, {once:true});
      worker.onmessage = ({data}) => {
        if(settled) return;
        if(data.error) finish(new Error('checksum_failed'));
        else if(data.sha256) {
          if(!/^[a-f0-9]{64}$/.test(data.sha256)) finish(new Error('checksum_failed'));
          else finish(null, data.sha256);
        } else progress(data.loaded, data.total);
      };
      worker.onerror = () => finish(new Error('checksum_failed'));
      if(signal.aborted) { cancel(); return; }
      worker.postMessage(file);
    } catch { finish(new Error('checksum_failed')); }
  });
}
function validUploadReceipt(receipt, size, sha256) {
  return receipt?.verified === true && receipt.size === size &&
    /^[a-f0-9]{64}$/.test(sha256) && receipt.sha256 === sha256 &&
    typeof receipt.id === 'string' && receipt.id.length > 0 &&
    typeof receipt.name === 'string' && receipt.name.length > 0;
}
