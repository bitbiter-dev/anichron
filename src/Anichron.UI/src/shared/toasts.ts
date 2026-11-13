import type { ParseKeys } from 'i18next';
import { UNSTABLE_ToastQueue as ToastQueue } from 'react-aria-components';

export interface ToastAction {
  label: ParseKeys;
  onAction: () => void;
}

export interface ToastMessage {
  message: ParseKeys;
  action?: ToastAction;
}

export type Toasts = ReturnType<typeof createToasts>;

const toastMilliseconds = 5000;

export function createToasts() {
  const queue = new ToastQueue<ToastMessage>();
  return {
    queue,
    show: (toast: ToastMessage) => queue.add(toast, { timeout: toastMilliseconds }),
    close: (key: string) => {
      queue.close(key);
    },
  };
}
