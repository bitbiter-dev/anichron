import { expect, it } from 'vitest';
import { advance } from '../../test/time';
import { createStory } from './story';

it('stops waiting for a picture to load once nobody listens', async () => {
  const story = createStory(
    [[{ kind: 'picture', durationSeconds: null }]],
    0,
    () => undefined,
    () => false,
  );
  const stop = story.subscribe(() => undefined);

  stop();
  await advance(60_000);

  expect(story.state().load).toBe('loading');
});
