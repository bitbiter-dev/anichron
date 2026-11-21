import { tv } from './tv';

const skeletonStyles = tv({ base: 'bg-skeleton skeleton' });

export function Skeleton({ className }: { className?: string }) {
  return <div aria-hidden className={skeletonStyles({ class: className })} />;
}
