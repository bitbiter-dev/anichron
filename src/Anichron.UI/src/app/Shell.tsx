import { Outlet, useRouter } from '@tanstack/react-router';
import { Toasts } from '../shared/ui/Toast';
import { BottomBar } from './BottomBar';
import { useScreen } from './screen';
import { TopBar } from './TopBar';
import { UpdateNotice } from './UpdateNotice';

export function Shell() {
  const screen = useScreen();
  const { toasts } = useRouter().options.context;
  return (
    <div className="mx-auto min-h-dvh max-w-5xl pb-24">
      {screen && <TopBar {...screen} />}
      <Outlet />
      <UpdateNotice />
      <Toasts toasts={toasts} />
      <BottomBar />
    </div>
  );
}
