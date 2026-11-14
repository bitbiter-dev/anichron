import {
  createBrowserHistory,
  createRootRouteWithContext,
  createRoute,
  createRouter,
  lazyRouteComponent,
  notFound,
  redirect,
  type RouterHistory,
} from '@tanstack/react-router';
import type { QueryClient } from '@tanstack/react-query';
import type { ParseKeys } from 'i18next';
import type { ApiClient } from '../api/client';
import { LoginPage } from '../features/login/LoginPage';
import { dayKey, isCalendarDate, storySearch } from '../features/on-this-day/day';
import { OnThisDayPage } from '../features/on-this-day/OnThisDayPage';
import { listSearch, workItemsKey } from '../features/work-items/route';
import { createThemeChoice, type ThemeChoice } from '../shared/theme';
import { createBack, type Back } from './back';
import { NotFound } from './NotFound';
import { returnPath } from './return-path';
import { Shell } from './Shell';
import { SignedIn } from './SignedIn';
import { Splash } from './Splash';
import { StartFailed } from './StartFailed';

interface RouterContext {
  api: ApiClient;
  queryClient: QueryClient;
  back: Back;
  theme: ThemeChoice;
}

function routeTree() {
  const rootRoute = createRootRouteWithContext<RouterContext>()({
    beforeLoad: ({ context }) => context.api.session.restore(),
    pendingComponent: Splash,
    errorComponent: StartFailed,
  });

  const signedInRoute = createRoute({
    getParentRoute: () => rootRoute,
    id: 'signed-in',
    component: SignedIn,
    beforeLoad: ({ context, location }) => {
      const user = context.api.session.user();
      if (!user)
        throw redirect({
          href: context.api.session.signedOut()
            ? '/login'
            : `/login?redirect=${encodeURIComponent(location.href)}`,
        });
      return { user };
    },
  });

  const shellRoute = createRoute({
    getParentRoute: () => signedInRoute,
    id: 'shell',
    component: Shell,
  });

  const onThisDayRoute = createRoute({
    getParentRoute: () => shellRoute,
    id: 'on-this-day',
    component: OnThisDayPage,
  });

  const homeRoute = createRoute({
    getParentRoute: () => onThisDayRoute,
    path: '/',
  });

  const storyRoute = createRoute({
    getParentRoute: () => onThisDayRoute,
    path: '/day/$date',
    validateSearch: storySearch,
    beforeLoad: ({ params }) => {
      if (!isCalendarDate(params.date)) throw notFound({ routeId: rootRoute.id });
    },
    onLeave: ({ context, params }) =>
      void context.queryClient.invalidateQueries({ queryKey: dayKey(params.date) }),
    component: lazyRouteComponent(() => import('../features/on-this-day/StoryPage'), 'StoryPage'),
  });

  const settingsRoute = createRoute({
    getParentRoute: () => shellRoute,
    path: '/settings',
    component: lazyRouteComponent(
      () => import('../features/settings/SettingsPage'),
      'SettingsPage',
    ),
    staticData: { screen: { title: 'settings.title' } },
  });

  const adminRoute = createRoute({
    getParentRoute: () => shellRoute,
    id: 'admin',
    beforeLoad: ({ context }) => {
      if (!context.user.isAdmin) throw notFound({ routeId: rootRoute.id });
    },
  });

  const workItemsRoute = createRoute({
    getParentRoute: () => adminRoute,
    path: '/admin/work-items',
    onLeave: ({ context }) => {
      context.queryClient.removeQueries({ queryKey: workItemsKey });
    },
    component: lazyRouteComponent(
      () => import('../features/work-items/WorkItemsPage'),
      'WorkItemsPage',
    ),
  });

  const workItemListRoute = createRoute({
    getParentRoute: () => workItemsRoute,
    path: '/',
    validateSearch: listSearch,
    component: lazyRouteComponent(
      () => import('../features/work-items/WorkItemListPage'),
      'WorkItemListPage',
    ),
    staticData: { screen: { title: 'workItems.title', parent: '/settings' } },
  });

  const workItemRoute = createRoute({
    getParentRoute: () => workItemsRoute,
    path: '$id',
    component: lazyRouteComponent(
      () => import('../features/work-items/WorkItemPage'),
      'WorkItemPage',
    ),
    staticData: { screen: { title: 'workItems.item', parent: '/admin/work-items' } },
  });

  const loginRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/login',
    beforeLoad: ({ context, search }) => {
      if (context.api.session.isSignedIn()) throw redirect({ href: returnPath(search) });
    },
    component: LoginPage,
  });

  return rootRoute.addChildren([
    signedInRoute.addChildren([
      shellRoute.addChildren([
        onThisDayRoute.addChildren([homeRoute, storyRoute]),
        settingsRoute,
        adminRoute.addChildren([workItemsRoute.addChildren([workItemListRoute, workItemRoute])]),
      ]),
    ]),
    loginRoute,
  ]);
}

export function createAppRouter(
  api: ApiClient,
  queryClient: QueryClient,
  history: RouterHistory = createBrowserHistory(),
) {
  const router = createRouter({
    routeTree: routeTree(),
    history,
    scrollRestoration: true,
    defaultNotFoundComponent: NotFound,
    context: { api, queryClient, back: createBack(history), theme: createThemeChoice() },
  });
  let userId: string | undefined;
  api.session.onChange(() => {
    const changedTo = api.session.user()?.id;
    if (changedTo !== userId) queryClient.clear();
    userId = changedTo;
    void router.invalidate();
  });
  return router;
}

declare module '@tanstack/react-router' {
  interface Register {
    router: ReturnType<typeof createAppRouter>;
  }
  interface StaticDataRouteOption {
    screen?: { title: ParseKeys; parent?: string };
  }
}
