import { Outlet, useRouter } from '@tanstack/react-router';
import { useSyncExternalStore } from 'react';
import { Splash } from './Splash';

export function SignedIn() {
  const { api } = useRouter().options.context;
  return useSyncExternalStore(api.onSessionChange, api.isSignedIn) ? <Outlet /> : <Splash />;
}
