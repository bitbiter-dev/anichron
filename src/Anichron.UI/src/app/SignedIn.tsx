import { Outlet } from '@tanstack/react-router';
import { useUser } from '../shared/use-user';
import { Splash } from './Splash';

export function SignedIn() {
  const user = useUser();
  return user ? <Outlet key={user.id} /> : <Splash />;
}
