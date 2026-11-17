export function takeOver() {
  return { type: 'SKIP_WAITING' };
}

export function asksToTakeOver(data: unknown) {
  return (
    typeof data === 'object' && data !== null && 'type' in data && data.type === takeOver().type
  );
}
