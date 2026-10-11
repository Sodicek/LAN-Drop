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
