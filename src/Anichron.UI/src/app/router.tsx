import {
  createRootRoute,
  createRoute,
  createRouter,
  type RouterHistory,
} from '@tanstack/react-router';
import { PlaceholderPage } from '../features/placeholder/PlaceholderPage';

const rootRoute = createRootRoute();

const placeholderRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  component: PlaceholderPage,
});

const routeTree = rootRoute.addChildren([placeholderRoute]);

export function createAppRouter(history?: RouterHistory) {
  return createRouter({ routeTree, history });
}

declare module '@tanstack/react-router' {
  interface Register {
    router: ReturnType<typeof createAppRouter>;
  }
}
