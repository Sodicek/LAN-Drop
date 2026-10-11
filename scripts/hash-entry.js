import { sha256 } from '@noble/hashes/sha2.js';
globalThis.createSha256 = () => sha256.create();
