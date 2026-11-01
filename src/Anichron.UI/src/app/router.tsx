import {
  createRootRouteWithContext,
  createRoute,
  createRouter,
  redirect,
  type RouterHistory,
} from '@tanstack/react-router';
import type { ApiClient } from '../api/client';
import { LoginPage } from '../features/login/LoginPage';
import { PlaceholderPage } from '../features/placeholder/PlaceholderPage';

interface RouterContext {
  api: ApiClient;
}

const rootRoute = createRootRouteWithContext<RouterContext>()();

const placeholderRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  beforeLoad: ({ context }) => {
    if (!context.api.isSignedIn()) throw redirect({ to: '/login' });
  },
  component: PlaceholderPage,
});

const loginRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/login',
  component: LoginPage,
});

const routeTree = rootRoute.addChildren([placeholderRoute, loginRoute]);

export function createAppRouter(api: ApiClient, history?: RouterHistory) {
  return createRouter({ routeTree, history, context: { api } });
}

declare module '@tanstack/react-router' {
  interface Register {
    router: ReturnType<typeof createAppRouter>;
  }
}
