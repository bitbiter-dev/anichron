import { Link } from '@tanstack/react-router';
import { CalendarHeart, Settings } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useHidesOnScroll } from './use-hides-on-scroll';
import { useLastHomeSearch } from './use-last-home-search';

export function BottomBar() {
  const { t } = useTranslation();
  const { hidden, show } = useHidesOnScroll();
  const barItems = [
    { to: '/', search: useLastHomeSearch(), label: 'shell.today', icon: CalendarHeart },
    { to: '/settings', search: {}, label: 'shell.settings', icon: Settings },
  ] as const;
  return (
    <nav
      data-hidden={hidden || undefined}
      className="fixed inset-x-4 bottom-[max(env(safe-area-inset-bottom),0.75rem)] mx-auto flex max-w-xs justify-around rounded-full p-1 glass-card transition-transform duration-(--duration-base) data-hidden:translate-y-[calc(100%+max(env(safe-area-inset-bottom),0.75rem)+0.25rem)]"
    >
      {barItems.map(({ to, search, label, icon: Icon }) => (
        <Link
          key={to}
          to={to}
          search={search}
          activeOptions={{ exact: true }}
          onFocus={show}
          className="flex min-h-11 min-w-16 flex-col items-center justify-center rounded-full px-4 py-1 text-small text-fg-muted outline-none focus-visible:focus-ring data-[status=active]:text-accent"
        >
          <Icon aria-hidden className="size-6" />
          {t(label)}
        </Link>
      ))}
    </nav>
  );
}
