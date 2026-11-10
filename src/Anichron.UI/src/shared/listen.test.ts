import { expect, it, vi } from 'vitest';
import { listen } from './listen';

it('passes each event of its type to the handler until stopped', () => {
  const target = new EventTarget();
  const handler = vi.fn();
  const stop = listen(target, 'change', handler);

  target.dispatchEvent(new Event('other'));
  target.dispatchEvent(new Event('change'));
  expect(handler).toHaveBeenCalledTimes(1);

  stop();
  target.dispatchEvent(new Event('change'));
  expect(handler).toHaveBeenCalledTimes(1);
});
