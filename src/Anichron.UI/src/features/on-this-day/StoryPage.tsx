import { useQuery } from '@tanstack/react-query';
import { getRouteApi, Navigate, useRouter } from '@tanstack/react-router';
import { X } from 'lucide-react';
import { Button, Dialog, Modal, ModalOverlay } from 'react-aria-components';
import { useTranslation } from 'react-i18next';
import type { YearGroup } from '../../api/flashbacks';
import { tv } from '../../shared/ui/tv';
import { dayQuery, yearsAgo } from './day';
import { dayView } from './day-view';
import { Failed } from './Failed';
import { ProxyImage } from './ProxyImage';
import { memoryMilliseconds } from './story';
import { useStory } from './use-story';

const story = getRouteApi('/signed-in/shell/on-this-day/day/$date');

export function StoryPage() {
  const { t } = useTranslation();
  const { date } = story.useParams();
  const { year } = story.useSearch();
  const { api, back } = useRouter().options.context;
  const day = useQuery({ ...dayQuery(api, date), refetchOnMount: false });
  const state = dayView(day);
  const close = () => {
    back.to('/');
  };
  return (
    <ModalOverlay isOpen onOpenChange={close} className="fixed inset-0 z-20 bg-player">
      <Modal className="size-full">
        <Dialog
          aria-label={t('story.title', {
            date: new Date(`${date}T00:00:00Z`),
            formatParams: { date: { dateStyle: 'long', timeZone: 'UTC' } },
          })}
          className="relative size-full text-on-player outline-none"
        >
          {state.years && <Player date={date} year={year} years={state.years} close={close} />}
          {state.empty && <Navigate to="/" replace />}
          {state.failure && (
            <div className="p-4 pt-16">
              <Failed message={state.failure} onRetry={() => void day.refetch()} />
            </div>
          )}
          <Button
            aria-label={t('story.close')}
            onPress={close}
            className="absolute top-[calc(env(safe-area-inset-top)+0.75rem)] right-2 flex size-11 items-center justify-center rounded-full outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
          >
            <X aria-hidden className="size-6" />
          </Button>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}

interface PlayerProps {
  date: string;
  year: number;
  years: YearGroup[];
  close: () => void;
}

function Player({ date, year, years, close }: PlayerProps) {
  const { t } = useTranslation();
  const { position, group, memory, mediaReady } = useStory(years, year, close);
  return (
    <>
      <ProxyImage
        key={memory.id}
        src={memory.proxies.preview}
        onDecoded={mediaReady}
        className="absolute inset-0 size-full object-contain"
      />
      <div className="absolute inset-x-0 top-0 flex flex-col gap-2 bg-linear-to-b from-player/60 px-2 pt-[calc(env(safe-area-inset-top)+0.5rem)] pb-6">
        <Segments
          count={group.memories.length}
          current={position.memory}
          running={position.ready}
        />
        <p className="pr-12 text-small font-semibold">
          {t('onThisDay.yearsAgo', yearsAgo(date, group.year))}
        </p>
      </div>
    </>
  );
}

const segment = tv({
  base: 'block h-full origin-left bg-on-player',
  variants: {
    state: {
      seen: 'scale-x-100',
      playing: 'animate-story-fill',
      next: 'scale-x-0',
    },
  },
});

interface SegmentsProps {
  count: number;
  current: number;
  running: boolean;
}

function Segments({ count, current, running }: SegmentsProps) {
  return (
    <ol aria-hidden className="flex gap-1">
      {Array.from({ length: count }, (_, index) => (
        <li key={index} className="h-0.5 flex-1 overflow-hidden rounded-full bg-on-player/40">
          <span
            style={{
              animationDuration: `${String(memoryMilliseconds)}ms`,
              animationPlayState: running ? 'running' : 'paused',
            }}
            className={segment({
              state: index < current ? 'seen' : index === current ? 'playing' : 'next',
            })}
          />
        </li>
      ))}
    </ol>
  );
}
