import { LivePhotoIcon } from './LivePhotoIcon';
import { tv } from './tv';

const badge = tv({ base: 'rounded-full p-0.5 text-on-player player-glass' });

interface LiveBadgeProps {
  className: string;
}

export function LiveBadge({ className }: LiveBadgeProps) {
  return <LivePhotoIcon className={badge({ class: className })} />;
}
