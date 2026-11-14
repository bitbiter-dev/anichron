import { expect, it, vi } from 'vitest';
import { createStory } from './story';

it('stops waiting for a picture to load once nobody listens', () => {
  const story = createStory([[true]], 0, () => undefined);
  const stop = story.subscribe(() => undefined);

  stop();
  vi.advanceTimersByTime(60_000);

  expect(story.state().load).toBe('loading');
});
