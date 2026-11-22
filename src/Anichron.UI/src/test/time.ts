import { userEvent } from '@testing-library/user-event';
import { act } from 'react';
import { vi } from 'vitest';

export async function advance(milliseconds: number) {
  await act(() => vi.advanceTimersByTimeAsync(milliseconds));
}

export async function runAllTimers() {
  await act(() => vi.runAllTimersAsync());
}

export function user() {
  return userEvent.setup({ advanceTimers: vi.advanceTimersByTime.bind(vi) });
}
