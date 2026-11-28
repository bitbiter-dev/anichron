import { mostPurgedAssets } from '../../api/generated/limits';
import type { ToastMessage } from '../../shared/toasts';

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
  for (let start = 0; start < ids.length; start += mostPurgedAssets) {
    const chunk = ids.slice(start, start + mostPurgedAssets);
    try {
      for (const id of await purge(chunk)) accepted.add(id);
    } catch {
      return { sent: ids.slice(0, start), accepted, unsent: ids.slice(start) };
    }
    onSent(chunk);
  }
  return { sent: ids, accepted, unsent: [] };
}

function sentToast(forgot: number, back: number): ToastMessage | null {
  if (back === 0)
    return forgot === 0 ? null : { message: 'removed.forgot', values: { count: forgot } };
  return forgot === 0
    ? { message: 'removed.notForgotten', values: { count: back } }
    : { message: 'removed.forgotSomeBack', values: { forgot, count: back } };
}

export function toastsOf({ sent, accepted, unsent }: Forgotten): ToastMessage[] {
  const forgot = sent.filter((id) => accepted.has(id)).length;
  const failed: ToastMessage | null =
    unsent.length === 0
      ? null
      : { message: 'removed.forgetFailed', values: { count: unsent.length } };
  return [sentToast(forgot, sent.length - forgot), failed].filter((toast) => toast !== null);
}
