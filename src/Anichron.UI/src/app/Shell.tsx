import { Outlet } from '@tanstack/react-router';
import { BottomBar } from './BottomBar';
import { useScreen } from './screen';
import { TopBar } from './TopBar';

export function Shell() {
  const screen = useScreen();
  return (
    <div className="mx-auto min-h-dvh max-w-5xl pb-24">
      {screen && <TopBar {...screen} />}
      <Outlet />
      <BottomBar />
    </div>
  );
}
