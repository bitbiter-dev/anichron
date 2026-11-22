import { expect, it, vi } from 'vitest';
import { becomes } from '../test/visibility';
import { createChanges, isVisible, listen, onVisibilityChange, onVisible } from './listen';

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

it('reads whether the page is visible', () => {
  becomes('hidden');
  expect(isVisible()).toBe(false);
  becomes('visible');
  expect(isVisible()).toBe(true);
});

it('tells of every visibility change until stopped', () => {
  const changed = vi.fn();
  const stop = onVisibilityChange(changed);

  becomes('hidden');
  becomes('visible');
  expect(changed).toHaveBeenCalledTimes(2);

  stop();
  becomes('hidden');
  expect(changed).toHaveBeenCalledTimes(2);
});

it('tells only of the page becoming visible, until stopped', () => {
  const visible = vi.fn();
  const stop = onVisible(visible);

  becomes('hidden');
  expect(visible).not.toHaveBeenCalled();
  becomes('visible');
  expect(visible).toHaveBeenCalledTimes(1);

  stop();
  becomes('hidden');
  becomes('visible');
  expect(visible).toHaveBeenCalledTimes(1);
});

it('tells each subscriber of a change until it unsubscribes', () => {
  const changes = createChanges();
  const first = vi.fn();
  const second = vi.fn();
  const stopFirst = changes.subscribe(first);
  changes.subscribe(second);
  expect(first).not.toHaveBeenCalled();

  changes.notify();
  stopFirst();
  changes.notify();

  expect(first).toHaveBeenCalledTimes(1);
  expect(second).toHaveBeenCalledTimes(2);
});
