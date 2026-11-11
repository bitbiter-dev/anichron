import { QueryClient } from '@tanstack/react-query';
import { SessionNotRenewed } from '../api/session';

const maxRetries = 3;

export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: (failures, error) => !(error instanceof SessionNotRenewed) && failures < maxRetries,
      },
    },
  });
}
