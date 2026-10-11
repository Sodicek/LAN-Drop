// File references stay in this tab only. Completed items are never retried.
class TransferQueue {
  constructor(files) { this.items = Array.from(files, file => ({file, state:'queued', error:null})); }
  remaining() { return this.items.filter(item => item.state !== 'completed'); }
  transition(item, state, error = null) {
    if(!this.items.includes(item) || item.state === 'completed') return;
    item.state = state; item.error = error;
  }
}
