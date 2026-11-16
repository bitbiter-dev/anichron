interface TrustedTypesFactory {
  createPolicy(
    name: string,
    rules: { createScriptURL(url: string): string },
  ): { createScriptURL(url: string): string };
}

export function registerServiceWorker(production: boolean) {
  if (!production || !('serviceWorker' in navigator)) return;
  const url = '/sw.js';
  const { trustedTypes } = globalThis as { trustedTypes?: TrustedTypesFactory };
  const scriptUrl =
    trustedTypes
      ?.createPolicy('service-worker', { createScriptURL: () => url })
      .createScriptURL(url) ?? url;
  navigator.serviceWorker.register(scriptUrl).catch(() => undefined);
}
