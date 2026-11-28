import { Volume2, VolumeX } from 'lucide-react';
import { Button } from 'react-aria-components';
import { useTranslation } from 'react-i18next';
import { LivePhotoIcon } from './LivePhotoIcon';

export type LiveState = 'still' | 'loading' | 'playing';

interface LiveButtonProps {
  shown: boolean;
  live: LiveState;
  play: () => void;
}

export function LiveButton({ shown, live, play }: LiveButtonProps) {
  const { t } = useTranslation();
  if (!shown) return null;
  return (
    <Button
      aria-label={t('story.playLivePhoto')}
      onPress={play}
      data-live={live}
      className="group pointer-events-auto flex h-11 items-center outline-none"
    >
      <span className="relative flex items-center gap-1 rounded-full py-0.5 pr-2 pl-1.5 text-small font-semibold player-glass group-data-focus-visible:focus-ring">
        <span
          aria-hidden
          className="pointer-events-none absolute -inset-px hidden live-ring rounded-full group-data-[live=loading]:block"
        />
        <LivePhotoIcon className="size-4 group-data-[live=playing]:animate-live-blink motion-reduce:group-data-[live=playing]:opacity-50" />
        {t('story.live')}
      </span>
    </Button>
  );
}

const soundIcons = { 'story.soundOn': VolumeX, 'story.soundOff': Volume2 };

interface SoundProps {
  shown: boolean;
  label: keyof typeof soundIcons;
  toggle: () => void;
}

export function Sound({ shown, label, toggle }: SoundProps) {
  const { t } = useTranslation();
  const Icon = soundIcons[label];
  if (!shown) return null;
  return (
    <Button
      aria-label={t(label)}
      onPress={toggle}
      className="absolute right-2 bottom-[calc(env(safe-area-inset-bottom)+1.25rem)] flex size-11 items-center justify-center rounded-full bg-player/40 outline-none focus-visible:focus-ring"
    >
      <Icon aria-hidden className="size-1/2" />
    </Button>
  );
}
