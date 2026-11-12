import { useQuery } from '@tanstack/react-query';
import { getRouteApi, Navigate, useRouter } from '@tanstack/react-router';
import { X } from 'lucide-react';
import { domMax, LazyMotion, m, MotionConfig } from 'motion/react';
import { Button, Dialog, Modal, ModalOverlay } from 'react-aria-components';
import { usePress } from 'react-aria/usePress';
import { useTranslation } from 'react-i18next';
import type { YearGroup } from '../../api/flashbacks';
import { dayQuery, utcDay, yearsAgo } from './day';
import { dayView } from './day-view';
import { Failed } from './Failed';
import { ProxyImage } from './ProxyImage';
import { memoryMilliseconds, segmentState } from './story';
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
            date: utcDay(date),
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
  const { position, group, memory, mediaReady, gestures } = useStory(years, year, close);
  return (
    <LazyMotion features={domMax} strict>
      <MotionConfig reducedMotion="user">
        <m.div
          drag
          dragConstraints={{ top: 0, right: 0, bottom: 0, left: 0 }}
          dragElastic={{ top: 0, right: 0, bottom: 1, left: 0 }}
          dragSnapToOrigin
          onDragStart={gestures.dragStart}
          onDragEnd={(_, { offset }) => {
            gestures.dragEnd(offset);
          }}
          className="absolute inset-0 select-none [-webkit-touch-callout:none]"
        >
          <ProxyImage
            key={position.visit}
            src={memory.proxies.preview}
            onDecoded={mediaReady}
            className="absolute inset-0 size-full object-contain"
          />
          <div className="absolute inset-0 flex">
            <Zone
              label={t('story.previous')}
              onPressStart={gestures.pressStart}
              onPress={gestures.tap('previous')}
            />
            <Middle onPressStart={gestures.pressStart} onPress={gestures.tap('next')} />
            <Zone
              label={t('story.next')}
              onPressStart={gestures.pressStart}
              onPress={gestures.tap('next')}
            />
          </div>
          <div className="pointer-events-none absolute inset-x-0 top-0 flex flex-col gap-2 bg-linear-to-b from-player/60 px-2 pt-[calc(env(safe-area-inset-top)+0.5rem)] pb-6">
            <Segments
              key={position.visit}
              count={group.memories.length}
              current={position.memory}
              running={position.ready}
            />
            <p className="pr-12 text-small font-semibold">
              {t('onThisDay.yearsAgo', yearsAgo(date, group.year))}
            </p>
          </div>
        </m.div>
      </MotionConfig>
    </LazyMotion>
  );
}

interface ZoneProps {
  label: string;
  onPressStart: () => void;
  onPress: () => void;
}

function Zone({ label, onPressStart, onPress }: ZoneProps) {
  return (
    <Button
      aria-label={label}
      onPressStart={onPressStart}
      onPress={onPress}
      className="h-full flex-1 outline-none focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-focus"
    />
  );
}

function Middle({ onPressStart, onPress }: Omit<ZoneProps, 'label'>) {
  const { pressProps } = usePress({ onPressStart, onPress });
  return <div {...pressProps} aria-hidden className="h-full flex-1" />;
}

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
            data-state={segmentState(index, current)}
            className="block h-full origin-left bg-on-player data-[state=next]:scale-x-0 data-[state=playing]:animate-story-fill data-[state=seen]:scale-x-100"
          />
        </li>
      ))}
    </ol>
  );
}
