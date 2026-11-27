import { setupServer } from 'msw/node';
import { refreshFails } from './handlers/auth';
import { noRecentDays, nothingOnThisDay } from './handlers/flashbacks';
import { proxyAnswers } from './handlers/proxies';

export const server = setupServer(
  refreshFails(),
  nothingOnThisDay(),
  noRecentDays(),
  proxyAnswers(),
);
