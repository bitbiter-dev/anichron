import {
  createBrowserHistory,
  createRootRouteWithContext,
  createRoute,
  createRouter,
  lazyRouteComponent,
  redirect,
  type RouterHistory,
} from '@tanstack/react-router';
import type { QueryClient } from '@tanstack/react-query';
import type { ParseKeys } from 'i18next';
import type { ApiClient } from '../api/client';
import { LoginPage } from '../features/login/LoginPage';
import { PlaceholderPage } from '../features/placeholder/PlaceholderPage';
import { createThemeChoice, type ThemeChoice } from '../shared/theme';
import { createBack, type Back } from './back';
import { returnPath } from './return-path';
import { Shell } from './Shell';
import { SignedIn } from './SignedIn';
import { Splash } from './Splash';

interface RouterContext {
  api: ApiClient;
  queryClient: QueryClient;
  back: Back;
  theme: ThemeChoice;
}

const rootRoute = createRootRouteWithContext<RouterContext>()({
  beforeLoad: ({ context }) => context.api.session.restore(),
  pendingComponent: Splash,
});

const signedInRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: 'signed-in',
  component: SignedIn,
  beforeLoad: ({ context, location }) => {
    if (!context.api.session.isSignedIn())
      throw redirect({ href: `/login?redirect=${encodeURIComponent(location.href)}` });
  },
});

const shellRoute = createRoute({
  getParentRoute: () => signedInRoute,
  id: 'shell',
  component: Shell,
});

const homeRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/',
  component: PlaceholderPage,
});

const settingsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/settings',
  component: lazyRouteComponent(() => import('../features/settings/SettingsPage'), 'SettingsPage'),
  staticData: { screen: { title: 'settings.title', parent: '/' } },
});

const loginRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/login',
  beforeLoad: ({ context, search }) => {
    if (context.api.session.isSignedIn()) throw redirect({ href: returnPath(search) });
  },
  component: LoginPage,
});

const routeTree = rootRoute.addChildren([
  signedInRoute.addChildren([shellRoute.addChildren([homeRoute, settingsRoute])]),
  loginRoute,
]);

export function createAppRouter(
  api: ApiClient,
  queryClient: QueryClient,
  history: RouterHistory = createBrowserHistory(),
) {
  const router = createRouter({
    routeTree,
    history,
    context: { api, queryClient, back: createBack(history), theme: createThemeChoice() },
  });
  api.session.onChange(() => void router.invalidate());
  return router;
}

declare module '@tanstack/react-router' {
  interface Register {
    router: ReturnType<typeof createAppRouter>;
  }
  interface StaticDataRouteOption {
    screen?: { title: ParseKeys; parent: string };
  }
}
