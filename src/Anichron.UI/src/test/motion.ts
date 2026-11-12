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
