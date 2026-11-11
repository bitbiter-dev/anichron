import { setupServer } from 'msw/node';
import { refreshFails } from './handlers/auth';
import { nothingOnThisDay } from './handlers/flashbacks';

export const server = setupServer(refreshFails(), nothingOnThisDay());
