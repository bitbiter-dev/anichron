export type TabMessage = 'signed-in' | 'signed-out';

export interface Tabs {
  exclusively<T>(task: () => Promise<T>): Promise<T>;
  announce(message: TabMessage): void;
  listen(handler: (message: TabMessage) => void): () => void;
}

export function browserTabs(): Tabs {
  const name = 'anichron-session';
  const channel = new BroadcastChannel(name);
  return {
    exclusively: (task) => navigator.locks.request(name, task),
    announce(message) {
      channel.postMessage(message);
    },
    listen(handler) {
      const receive = (event: MessageEvent<TabMessage>) => {
        handler(event.data);
      };
      channel.addEventListener('message', receive);
      return () => {
        channel.removeEventListener('message', receive);
      };
    },
  };
}
