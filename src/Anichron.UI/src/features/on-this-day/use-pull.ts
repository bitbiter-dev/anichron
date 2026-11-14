import { animate, type PanInfo, useMotionValue, useTransform } from 'motion/react';
import type { Gestures } from './navigation';

const cardScale = 0.6;
const cardRadius = 24;

function reducesMotion() {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

export function pullLook(pulled: number, height: number, reduced: boolean) {
  const share = Math.min(pulled / height, 1);
  const shrinks = !reduced && share > 0;
  return {
    backdrop: 1 - share,
    scale: shrinks ? 1 - share * (1 - cardScale) : 1,
    radius: shrinks ? cardRadius : 0,
  };
}

export function usePull(gestures: Gestures) {
  const reduced = reducesMotion();
  const pulled = useMotionValue(0);
  const look = (value: number) => pullLook(value, window.innerHeight, reducesMotion());
  return {
    elastic: { top: 0, right: 0, bottom: reduced ? 0 : 1, left: 0 },
    card: {
      scale: useTransform(pulled, (value) => look(value).scale),
      borderRadius: useTransform(pulled, (value) => look(value).radius),
    },
    backdrop: { opacity: useTransform(pulled, (value) => look(value).backdrop) },
    onDrag: (_: unknown, { offset }: PanInfo) => {
      pulled.set(Math.max(0, offset.y));
    },
    onDragEnd: (_: unknown, { offset, velocity }: PanInfo) => {
      if (!gestures.dragEnd(offset, velocity, window.innerHeight)) void animate(pulled, 0);
    },
  };
}
