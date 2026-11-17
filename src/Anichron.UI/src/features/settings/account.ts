import { useUser } from '../../shared/use-user';

export function useAccount() {
  const user = useUser();
  if (!user) return undefined;
  return {
    name: user.username,
    initial: user.username.charAt(0).toUpperCase(),
    isAdmin: user.isAdmin,
  };
}
