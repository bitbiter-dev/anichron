import { EventSourceParserStream } from 'eventsource-parser/stream';
import { createBackoff } from '../shared/backoff';
import { problemOf } from './problem';

type Open = (lastEventId: string) => Promise<ReadableStream<BufferSource> | null>;

export interface Received<T> {
  event(id: string, data: T): void;
  reconnecting(waiting: boolean): void;
  gone(): void;
}

function pause(milliseconds: number, signal: AbortSignal) {
  return new Promise<boolean>((resolve) => {
    setTimeout(() => {
      resolve(!signal.aborted);
    }, milliseconds);
  });
}

export async function follow(
  open: Open,
  from: string,
  signal: AbortSignal,
  received: Received<string>,
) {
  let lastEventId = from;
  const backoff = createBackoff({ firstSeconds: 1, longestSeconds: 30 });
  for (;;) {
    let wait: number;
    try {
      const body = await open(lastEventId);
      if (!body) {
        received.gone();
        return;
      }
      backoff.reset();
      const events = body
        .pipeThrough(new TextDecoderStream())
        .pipeThrough(new EventSourceParserStream())
        .getReader();
      for (let next = await events.read(); !next.done; next = await events.read()) {
        lastEventId = next.value.id ?? lastEventId;
        received.event(lastEventId, next.value.data);
      }
      wait = backoff.waitMilliseconds(0);
    } catch (error) {
      wait = backoff.waitMilliseconds(problemOf(error).retryAfterSeconds);
    }
    if (signal.aborted) return;
    received.reconnecting(true);
    if (!(await pause(wait, signal))) return;
    received.reconnecting(false);
  }
}
