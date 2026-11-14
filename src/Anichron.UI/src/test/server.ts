import { setupServer } from 'msw/node';
import { refreshFails } from './handlers/auth';
import { nothingOnThisDay } from './handlers/flashbacks';
import { proxyAnswers } from './handlers/proxies';

export const server = setupServer(refreshFails(), nothingOnThisDay(), proxyAnswers());
