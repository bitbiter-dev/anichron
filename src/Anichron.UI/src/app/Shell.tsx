import { Outlet, useRouter } from '@tanstack/react-router';
import { TopBarSlotsContext, useTopBarSlots } from '../shared/top-bar';
import { Toasts } from '../shared/ui/Toast';
import { BottomBar } from './BottomBar';
import { useScreen } from './screen';
import { TopBar } from './TopBar';
import { UpdateNotice } from './UpdateNotice';

export function Shell() {
  const screen = useScreen();
  const { toasts } = useRouter().options.context;
  const { slots, ...bar } = useTopBarSlots();
  return (
    <div className="mx-auto min-h-dvh max-w-5xl pb-24">
      <TopBarSlotsContext value={slots}>
        {screen && <TopBar {...screen} {...bar} />}
        <Outlet />
      </TopBarSlotsContext>
      <UpdateNotice />
      <Toasts toasts={toasts} />
      <BottomBar />
    </div>
  );
}
