// Motion keeps the requestAnimationFrame it finds on loading, and waits for a frame it asked for
// before asking again; this one asks each test's fake clock, and carries a waiting frame over.
let waiting: FrameRequestCallback | undefined;

function onCurrentClock(callback: FrameRequestCallback) {
  waiting = callback;
  return globalThis.requestAnimationFrame((time) => {
    waiting = undefined;
    callback(time);
  });
}

const { requestAnimationFrame } = globalThis;
globalThis.requestAnimationFrame = onCurrentClock;
await import('motion/react');
globalThis.requestAnimationFrame = requestAnimationFrame;

export function resumeMotionFrames() {
  if (waiting) onCurrentClock(waiting);
}
