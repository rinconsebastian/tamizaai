/** jsdom has no secure-context flag and no WebCrypto subtle API; browsers on HTTPS or localhost have both. */
export function simulateSecureContext(): void {
  Object.defineProperty(window, 'isSecureContext', { value: true, configurable: true });
  if (!window.crypto.subtle) {
    Object.defineProperty(window.crypto, 'subtle', { value: {}, configurable: true });
  }
}
