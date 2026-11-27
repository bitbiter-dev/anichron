import { useRouter } from '@tanstack/react-router';
import { useState } from 'react';
import { forgetInChunks, toastsOf } from './forget';

interface ForgetEvents {
  sent: (ids: readonly string[]) => void;
  refused: () => void;
  done: () => void;
}

export function useForget(on: ForgetEvents) {
  const { api, toasts } = useRouter().options.context;
  const [asked, setAsked] = useState<readonly string[] | null>(null);

  async function forget(ids: readonly string[]) {
    const outcome = await forgetInChunks(ids, (chunk) => api.softDeleted.purge(chunk), on.sent);
    for (const toast of toastsOf(outcome)) toasts.show(toast);
    if (outcome.sent.some((id) => !outcome.accepted.has(id))) on.refused();
    if (outcome.unsent.length === 0) on.done();
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
      void forget(asked as readonly string[]);
    },
  };
}
