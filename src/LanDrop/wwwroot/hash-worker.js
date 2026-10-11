importScripts('/vendor/sha256.js');
self.onmessage = async ({data:file}) => {
  try {
    const hash = createSha256();
    const chunkSize = 1024 * 1024;
    let lastReport = -Infinity;
    for(let offset = 0; offset < file.size; offset += chunkSize) {
      const end = Math.min(file.size, offset + chunkSize);
      hash.update(new Uint8Array(await file.slice(offset, end).arrayBuffer()));
      const now = performance.now();
      if(now - lastReport >= 250 || end === file.size) {
        self.postMessage({loaded:end, total:file.size}); lastReport = now;
      }
    }
    const sha256 = Array.from(hash.digest(), byte => byte.toString(16).padStart(2, '0')).join('');
    self.postMessage({sha256});
  } catch { self.postMessage({error:true}); }
};
