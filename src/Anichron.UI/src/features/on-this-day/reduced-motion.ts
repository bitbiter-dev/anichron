export function reducesMotion() {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}
