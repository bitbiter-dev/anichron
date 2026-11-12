import { EventSourceParserStream } from 'eventsource-parser/stream';
import { problemOf } from './problem';

type Open = (lastEventId: string) => Promise<ReadableStream<BufferSource> | null>;

export interface Received<T> {
  event(id: string, data: T): void;
  reconnecting(waiting: boolean): void;
  gone(): void;
}

const firstBackoff = 1000;
const maxBackoff = 30_000;

const backoff = (failures: number) => Math.min(firstBackoff * 2 ** (failures - 1), maxBackoff);

const pause = (milliseconds: number) =>
  new Promise((resolve) => {
    setTimeout(resolve, milliseconds);
  });

export async function follow(
  open: Open,
  from: string,
  signal: AbortSignal,
  received: Received<string>,
) {
  let lastEventId = from;
  let failures = 0;
  for (;;) {
    let wait: number;
    try {
      const body = await open(lastEventId);
      if (!body) {
        received.gone();
        return;
      }
      failures = 0;
      const events = body
        .pipeThrough(new TextDecoderStream())
        .pipeThrough(new EventSourceParserStream())
        .getReader();
      for (let next = await events.read(); !next.done; next = await events.read()) {
        lastEventId = next.value.id ?? lastEventId;
        received.event(lastEventId, next.value.data);
      }
      wait = backoff(++failures);
    } catch (error) {
      wait = problemOf(error).retryAfterSeconds * 1000 || backoff(++failures);
    }
    if (signal.aborted) return;
    received.reconnecting(true);
    await pause(wait);
    received.reconnecting(false);
  }
}
