import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { browserTabs } from './tabs';

class FakeChannel extends EventTarget {
  static opened: FakeChannel[] = [];
  readonly posted: unknown[] = [];
  readonly name: string;

  constructor(name: string) {
    super();
    this.name = name;
    FakeChannel.opened.push(this);
  }

  postMessage(message: unknown) {
    this.posted.push(message);
  }
}

const request = vi.fn((_name: string, task: () => Promise<unknown>) => task());

beforeEach(() => {
  FakeChannel.opened = [];
  vi.stubGlobal('BroadcastChannel', FakeChannel);
  Object.defineProperty(navigator, 'locks', { value: { request }, configurable: true });
});

afterEach(() => {
  vi.unstubAllGlobals();
  Reflect.deleteProperty(navigator, 'locks');
  request.mockClear();
});

const channel = () => FakeChannel.opened[0];

describe('the browser tabs', () => {
  it('run a task under the session Web Lock and answer with its result', async () => {
    const tabs = browserTabs();

    expect(await tabs.exclusively(() => Promise.resolve('renewed'))).toBe('renewed');
    expect(request).toHaveBeenCalledOnce();
    expect(request.mock.calls[0]?.[0]).toBe('anichron-session');
  });

  it('announce on the session channel', () => {
    browserTabs().announce('signed-out');

    expect(FakeChannel.opened).toHaveLength(1);
    expect(channel()?.name).toBe('anichron-session');
    expect(channel()?.posted).toEqual(['signed-out']);
  });

  it('hand every message from another tab to the listener until it stops listening', () => {
    const heard: string[] = [];
    const stop = browserTabs().listen((message) => heard.push(message));
    const from = channel();
    if (!from) throw new Error('no channel was opened');

    from.dispatchEvent(new MessageEvent('message', { data: 'signed-in' }));
    from.dispatchEvent(new MessageEvent('message', { data: 'signed-out' }));
    stop();
    from.dispatchEvent(new MessageEvent('message', { data: 'signed-in' }));

    expect(heard).toEqual(['signed-in', 'signed-out']);
  });
});
