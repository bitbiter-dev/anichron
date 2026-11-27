import { Outlet, useRouter } from '@tanstack/react-router';
import { TopBarSlotsContext, useTopBarSlots } from '../shared/top-bar';
import { Toasts } from '../shared/ui/Toast';
import { BottomBar } from './BottomBar';
import { useScreen } from './screen';
import { useHidesOnScroll } from './use-hides-on-scroll';
import { TopBar } from './TopBar';
import { UpdateNotice } from './UpdateNotice';

export function Shell() {
  const screen = useScreen();
  const { toasts } = useRouter().options.context;
  const { slots, ...bar } = useTopBarSlots();
  const bottomBar = useHidesOnScroll();
  return (
    <div
      data-bar-hidden={bottomBar.hidden || undefined}
      className="mx-auto min-h-dvh max-w-(--shell-width) pb-24 [--shell-width:var(--container-5xl)]"
    >
      <TopBarSlotsContext value={slots}>
        {screen && <TopBar {...screen} {...bar} />}
        <Outlet />
      </TopBarSlotsContext>
      <UpdateNotice />
      <Toasts toasts={toasts} />
      <BottomBar {...bottomBar} />
    </div>
  );
}
