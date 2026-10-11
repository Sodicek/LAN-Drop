# Upload integrity

The sender calculates SHA-256 before uploading. Windows keeps the source open while hashing, rewinds that same handle and uploads from it. The browser reads one-megabyte slices in a dedicated worker, using the locally bundled `@noble/hashes` implementation. Preparation has its own progress and can be canceled; upload speed starts only when sending begins.

Every `PUT /api/files` requires `X-LanDrop-SHA256` with exactly 64 hexadecimal characters. Missing/malformed checksums return `400 checksum_required` before consuming a receive offer. Phone uploads still need their approved offer and matching name/size. An actual attempted upload consumes approval even if integrity validation fails.

The server hashes bytes as they are written, checks declared length, closes the temporary file, compares the stream hash, and reads the temporary file again to check its saved contents. A mismatch returns `422 checksum_mismatch`; the temporary file and byte/file reservations are released. Only after both comparisons and cancellation/authorization checks does the server move the file to its final name and register it.

A successful response includes `{id, name, size, sha256, verified: true}`. Clients require a matching size/checksum and verified status before reporting success. File metadata retains that commit-time checksum. If the response is lost or invalid, the file may already exist on the PC: inspect the transfer list before retrying to avoid duplicates.

This is accidental-corruption detection, not authenticated encryption. An attacker on HTTP can replace the page, data, checksum or receipt. Readback can come from the operating-system cache and does not guarantee power-loss durability. Later modifications are not automatically detected. Safari downloads use the browser's native save flow and are not independently checksum-verified by LAN Drop.

## Browser asset maintenance

The repository pins `@noble/hashes` and the build tool in `package-lock.json`. The source entry is `scripts/hash-entry.js`. Run `npm ci --ignore-scripts` then `npm run build:hash` to regenerate `wwwroot/vendor/sha256.js` and its license. CI regenerates these assets and rejects drift. Normal application use needs no npm installation, CDN or internet access.

Tests compare the actual bundled worker against known vectors and Node's SHA-256, including padding boundaries and multiple chunks. Additional tests cover cancellation, receipts, corrupt uploads, offer reuse and quota release. Physical iPhone/Safari and large-file benchmarks remain pending.
