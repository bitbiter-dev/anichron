import { expect, it, vi } from 'vitest';
import { createGestures } from './navigation';
import { createStory } from './story';

it.each([
  [800, 1],
  [799.9, 0],
])('closes on a flick down at %f px/s or faster', (speed, closes) => {
  const close = vi.fn();
  const gestures = createGestures(createStory([['none']], 0, close));

  gestures.dragEnd({ x: 0, y: 60 }, { x: 0, y: speed }, 768);

  expect(close).toHaveBeenCalledTimes(closes);
});
