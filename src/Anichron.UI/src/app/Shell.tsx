import { Outlet } from '@tanstack/react-router';
import { BottomBar } from './BottomBar';
import { useScreen } from './screen';
import { TopBar } from './TopBar';

export function Shell() {
  const screen = useScreen();
  return screen ? (
    <div className="mx-auto min-h-dvh max-w-5xl">
      <TopBar {...screen} />
      <Outlet />
    </div>
  ) : (
    <div className="mx-auto min-h-dvh max-w-5xl pb-24">
      <Outlet />
      <BottomBar />
    </div>
  );
}
