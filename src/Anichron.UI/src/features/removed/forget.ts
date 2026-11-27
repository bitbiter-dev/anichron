import type { ToastMessage } from '../../shared/toasts';

export const chunkSize = 1000;

export interface Forgotten {
  sent: readonly string[];
  accepted: ReadonlySet<string>;
  unsent: readonly string[];
}

export async function forgetInChunks(
  ids: readonly string[],
  purge: (chunk: readonly string[]) => Promise<readonly string[]>,
  onSent: (chunk: readonly string[]) => void,
): Promise<Forgotten> {
  const accepted = new Set<string>();
  for (let start = 0; start < ids.length; start += chunkSize) {
    const chunk = ids.slice(start, start + chunkSize);
    try {
      for (const id of await purge(chunk)) accepted.add(id);
    } catch {
      return { sent: ids.slice(0, start), accepted, unsent: ids.slice(start) };
    }
    onSent(chunk);
  }
  return { sent: ids, accepted, unsent: [] };
}

export function toastsOf({ sent, accepted, unsent }: Forgotten): ToastMessage[] {
  const forgot = sent.filter((id) => accepted.has(id)).length;
  const back = sent.length - forgot;
  const toasts: ToastMessage[] = [];
  if (sent.length > 0)
    toasts.push(
      back === 0
        ? { message: 'removed.forgot', values: { count: forgot } }
        : { message: 'removed.forgotSomeBack', values: { forgot, count: back } },
    );
  if (unsent.length > 0)
    toasts.push({ message: 'removed.forgetFailed', values: { count: unsent.length } });
  return toasts;
}
