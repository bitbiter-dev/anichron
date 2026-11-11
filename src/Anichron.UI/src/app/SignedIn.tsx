import { Outlet } from '@tanstack/react-router';
import { useUser } from '../shared/use-user';
import { Splash } from './Splash';

export function SignedIn() {
  return useUser() ? <Outlet /> : <Splash />;
}
