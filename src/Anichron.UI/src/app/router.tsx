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
import { returnPath } from './return-path';
import { Splash } from './Splash';

interface RouterContext {
  api: ApiClient;
}

const rootRoute = createRootRouteWithContext<RouterContext>()({
  beforeLoad: ({ context }) => context.api.restore(),
  pendingComponent: Splash,
});

const signedInRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: 'signed-in',
  beforeLoad: ({ context, location }) => {
    if (!context.api.isSignedIn())
      throw redirect({ href: `/login?redirect=${encodeURIComponent(location.href)}` });
  },
});

const placeholderRoute = createRoute({
  getParentRoute: () => signedInRoute,
  path: '/',
  component: PlaceholderPage,
});

const loginRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/login',
  beforeLoad: ({ context, search }) => {
    if (context.api.isSignedIn()) throw redirect({ href: returnPath(search) });
  },
  component: LoginPage,
});

const routeTree = rootRoute.addChildren([
  signedInRoute.addChildren([placeholderRoute]),
  loginRoute,
]);

export function createAppRouter(api: ApiClient, history?: RouterHistory) {
  return createRouter({ routeTree, history, context: { api } });
}

declare module '@tanstack/react-router' {
  interface Register {
    router: ReturnType<typeof createAppRouter>;
  }
}
