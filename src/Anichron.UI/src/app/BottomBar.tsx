import { Link } from '@tanstack/react-router';
import { CalendarHeart, Settings } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useHidesOnScroll } from './use-hides-on-scroll';

const barItems = [
  { to: '/', label: 'shell.today', icon: CalendarHeart },
  { to: '/settings', label: 'shell.settings', icon: Settings },
] as const;

export function BottomBar() {
  const { t } = useTranslation();
  const { hidden, show } = useHidesOnScroll();
  return (
    <nav
      data-hidden={hidden || undefined}
      className="fixed inset-x-4 bottom-[calc(env(safe-area-inset-bottom)+0.75rem)] mx-auto flex max-w-xs justify-around rounded-full p-1 glass-card transition-transform duration-(--duration-base) data-hidden:translate-y-[calc(100%+env(safe-area-inset-bottom)+1rem)]"
    >
      {barItems.map(({ to, label, icon: Icon }) => (
        <Link
          key={to}
          to={to}
          activeOptions={{ exact: true }}
          onFocus={show}
          className="flex min-h-11 min-w-16 flex-col items-center justify-center rounded-full px-4 py-1 text-small text-fg-muted outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus data-[status=active]:text-accent"
        >
          <Icon aria-hidden className="size-6" />
          {t(label)}
        </Link>
      ))}
    </nav>
  );
}
