import { expect, it, vi } from 'vitest';
import { createStory } from './story';

it('stops waiting for a picture to load once nobody listens', () => {
  const story = createStory(
    [[{ kind: 'picture', durationSeconds: null }]],
    0,
    () => undefined,
    () => false,
  );
  const stop = story.subscribe(() => undefined);

  stop();
  vi.advanceTimersByTime(60_000);

  expect(story.state().load).toBe('loading');
});
