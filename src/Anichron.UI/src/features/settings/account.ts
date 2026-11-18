import type { User } from '../../api/session';
import { useUser } from '../../shared/use-user';

export function useAccount() {
  const { username, isAdmin } = useUser() as User;
  return { name: username, initial: username.charAt(0).toUpperCase(), isAdmin };
}
