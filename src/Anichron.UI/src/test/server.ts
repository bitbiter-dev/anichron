import { setupServer } from 'msw/node';
import { meAnswersFor, refreshFails } from './handlers/auth';

export const server = setupServer(refreshFails(), meAnswersFor());
