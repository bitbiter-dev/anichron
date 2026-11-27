import { useRouter } from '@tanstack/react-router';
import { useState } from 'react';
import { forgetInChunks, toastsOf } from './forget';
import { removedKey } from './removed';

export function useForget(onSent: (sent: readonly string[]) => void, onDone: () => void) {
  const { api, queryClient, toasts } = useRouter().options.context;
  const [asked, setAsked] = useState<readonly string[] | null>(null);

  async function forget(ids: readonly string[]) {
    const outcome = await forgetInChunks(ids, (chunk) => api.softDeleted.purge(chunk), onSent);
    for (const toast of toastsOf(outcome)) toasts.show(toast);
    if (outcome.sent.some((id) => !outcome.accepted.has(id)))
      void queryClient.invalidateQueries({ queryKey: removedKey() });
    if (outcome.unsent.length === 0) onDone();
  }

  return {
    asked: asked?.length ?? 0,
    ask: (ids: readonly string[]) => () => {
      setAsked(ids);
    },
    cancel: () => {
      setAsked(null);
    },
    confirm: () => {
      setAsked(null);
      void forget(asked ?? []);
    },
  };
}
