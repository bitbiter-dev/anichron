import { useRouter } from '@tanstack/react-router';
import { useState } from 'react';
import type { User } from '../api/session';
import { useUser } from '../shared/use-user';

export type NoPhotos = ReturnType<typeof createNoPhotos>;

export function createNoPhotos() {
  let dismissed = false;
  return {
    dismissed: () => dismissed,
    dismiss() {
      dismissed = true;
    },
  };
}

export function useHome() {
  const { noPhotos } = useRouter().options.context;
  const { hasStorageConfig } = useUser() as User;
  const [open, setOpen] = useState(() => !noPhotos.dismissed());
  return {
    onThisDay: hasStorageConfig,
    notice: open && !hasStorageConfig,
    dismiss: () => {
      noPhotos.dismiss();
      setOpen(false);
    },
  };
}
