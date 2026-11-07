import { setupServer } from 'msw/node';
import { refreshFails } from './handlers/auth';

export const server = setupServer(refreshFails());
