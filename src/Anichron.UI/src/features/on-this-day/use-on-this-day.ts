import type { User } from '../../api/session';
import { useUser } from '../../shared/use-user';
import { hasPhotos } from './day';
import { useToday } from './use-today';

export function useOnThisDay() {
  const user = useUser() as User;
  const today = useToday();
  return hasPhotos(user)
    ? ({ shows: 'day', today } as const)
    : ({ shows: 'noPhotos', hint: !user.isAdmin } as const);
}
