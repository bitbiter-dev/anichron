export function createBackoff({
  firstSeconds,
  longestSeconds,
}: Record<'firstSeconds' | 'longestSeconds', number>) {
  let failures = 0;
  return {
    waitMilliseconds(retryAfterSeconds: number) {
      const seconds = retryAfterSeconds || Math.min(firstSeconds * 2 ** failures++, longestSeconds);
      return seconds * 1000;
    },
    reset() {
      failures = 0;
    },
  };
}
