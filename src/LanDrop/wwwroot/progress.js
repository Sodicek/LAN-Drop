// Measures bytes sent by this client; server acknowledgement remains a separate state.
class TransferMeter {
  constructor(total, now = () => performance.now()) {
    this.total = Math.max(0, total);
    this.now = now;
    this.started = now();
    this.loaded = 0;
  }
  sample(bytes) {
    this.loaded = Math.min(this.total, Math.max(this.loaded, Number.isFinite(bytes) ? bytes : 0));
    const seconds = Math.max(0, (this.now() - this.started) / 1000);
    const speed = seconds >= .25 ? this.loaded / seconds : 0;
    return {
      loaded: this.loaded,
      total: this.total,
      percent: this.total === 0 ? 100 : this.loaded / this.total * 100,
      speed,
      remainingSeconds: speed > 0 ? Math.ceil((this.total - this.loaded) / speed) : null
    };
  }
}
