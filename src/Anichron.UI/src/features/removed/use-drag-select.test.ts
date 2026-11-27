import { describe, expect, it } from 'vitest';
import { edgeDirection } from './use-drag-select';

describe('edgeDirection', () => {
  it.each([
    [0, -1],
    [79, -1],
    [80, 0],
    [400, 0],
    [720, 0],
    [721, 1],
    [800, 1],
  ])('at %i px of an 800 px window scrolls %i', (y, direction) => {
    expect(edgeDirection(y, 800)).toBe(direction);
  });
});
