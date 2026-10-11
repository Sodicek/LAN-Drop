// Metadata is offered first. No file body is sent until the PC accepts it.
class ReceiveOfferClient {
  constructor(request, delay = ReceiveOfferClient.delay) { this.request = request; this.delay = delay; }
  static delay(milliseconds, signal) {
    return new Promise((resolve, reject) => {
      const abort = () => { clearTimeout(timer); signal.removeEventListener('abort', abort); reject(new DOMException('Cancelled', 'AbortError')); };
      const timer = setTimeout(() => { signal.removeEventListener('abort', abort); resolve(); }, milliseconds);
      signal.addEventListener('abort', abort, {once:true});
      if(signal.aborted) abort();
    });
  }
  async approve(file, signal, onWaiting) {
    let id, accepted = false;
    try {
      if(signal.aborted) throw new DOMException('Cancelled', 'AbortError');
      const offer = await this.request('/api/offers', {
        method:'POST', headers:{'Content-Type':'application/json'},
        body:JSON.stringify({name:file.name, size:file.size}), signal
      });
      id = offer.id;
      onWaiting();
      let state = offer;
      while(true) {
        if(signal.aborted) throw new DOMException('Cancelled', 'AbortError');
        if(state.status === 'approved') { accepted = true; return id; }
        if(state.status !== 'pending') {
          const error = new Error('Offer ' + state.status);
          error.code = state.status === 'rejected' ? 'offer_rejected' : state.status === 'expired' ? 'offer_expired' : 'offer_not_approved';
          throw error;
        }
        await this.delay(1000, signal);
        state = await this.request('/api/offers/' + encodeURIComponent(id), {signal});
      }
    } finally {
      if(id && !accepted) await this.request('/api/offers/' + encodeURIComponent(id), {method:'DELETE'}).catch(() => {});
    }
  }
}
class BatchOfferClient {
  constructor(request, delay = ReceiveOfferClient.delay) { this.request = request; this.delay = delay; }
  async approve(files, signal, onWaiting) {
    let batch, accepted = false;
    try {
      if(signal.aborted) throw new DOMException('Cancelled', 'AbortError');
      batch = await this.request('/api/offer-batches', {method:'POST',
        headers:{'Content-Type':'application/json'}, body:JSON.stringify({files:files.map(file => ({name:file.name,size:file.size}))}), signal});
      onWaiting();
      let status = batch.status;
      while(true) {
        if(signal.aborted) throw new DOMException('Cancelled', 'AbortError');
        if(status === 'approved') {
          if(!Array.isArray(batch.offers) || batch.offers.length !== files.length || batch.offers.some((offer,i) =>
            !offer.id || offer.name !== files[i].name || offer.size !== files[i].size)) throw new Error('offer_mismatch');
          accepted = true; return batch;
        }
        if(status !== 'pending') {
          const error = new Error('Batch ' + status);
          error.code = status === 'rejected' ? 'offer_rejected' : status === 'expired' ? 'offer_expired' : 'offer_not_approved';
          throw error;
        }
        await this.delay(1000, signal);
        status = (await this.request('/api/offer-batches/'+encodeURIComponent(batch.id), {signal})).status;
      }
    } finally {
      if(batch?.id && !accepted) await this.request('/api/offer-batches/'+encodeURIComponent(batch.id), {method:'DELETE'}).catch(()=>{});
    }
  }
}
