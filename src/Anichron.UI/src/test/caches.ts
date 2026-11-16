const keyOf = (request: RequestInfo | URL) =>
  new URL(typeof request === 'object' && 'url' in request ? request.url : request, location.href)
    .href;

function memoryCache(): Cache {
  const entries = new Map<string, { request: Request; response: Response }>();
  const cache: Partial<Cache> = {
    match(request) {
      return Promise.resolve(entries.get(keyOf(request))?.response.clone());
    },
    put(request, response) {
      const key = keyOf(request);
      entries.set(key, { request: new Request(key), response: response.clone() });
      return Promise.resolve();
    },
    delete(request) {
      return Promise.resolve(entries.delete(keyOf(request)));
    },
    keys() {
      return Promise.resolve([...entries.values()].map(({ request }) => request));
    },
  };
  return cache as Cache;
}

export function memoryCaches(): CacheStorage {
  const named = new Map<string, Cache>();
  const open = (name: string) => {
    const cache = named.get(name) ?? memoryCache();
    named.set(name, cache);
    return cache;
  };
  const storage: Partial<CacheStorage> = {
    open: (name) => Promise.resolve(open(name)),
    has: (name) => Promise.resolve(named.has(name)),
    delete: (name) => Promise.resolve(named.delete(name)),
    keys: () => Promise.resolve([...named.keys()]),
    async match(request) {
      for (const cache of named.values()) {
        const response = await cache.match(request);
        if (response) return response;
      }
      return undefined;
    },
  };
  return storage as CacheStorage;
}
