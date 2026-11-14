import { useQuery } from '@tanstack/react-query';
import { getRouteApi, Navigate, useRouter } from '@tanstack/react-router';
import { Pause, Play, Volume2, VolumeX, X } from 'lucide-react';
import { domMax, LazyMotion, m, MotionConfig } from 'motion/react';
import { Button, Dialog, Modal, ModalOverlay } from 'react-aria-components';
import { useTranslation } from 'react-i18next';
import type { Day } from '../../api/flashbacks';
import { Toasts } from '../../shared/ui/Toast';
import { paintBlurhash } from './blurhash';
import { dayQuery, interactionOf, utcDay, yearsAgo } from './day';
import { type DayView, dayView } from './day-view';
import { Failed } from './Failed';
import { focusOnMount } from './focus';
import { MemoryActions } from './MemoryActions';
import { ProxyImage } from './ProxyImage';
import type { Gestures } from './navigation';
import { type Move, segmentState, type Tap } from './story';
import { useInteractions } from './use-interactions';
import { useStory } from './use-story';
import { useZonePress } from './use-zone-press';

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
          {state.years ? (
            <Player date={date} year={year} day={day.data as Day} close={close} />
          ) : (
            <Fallback state={state} close={close} retry={() => void day.refetch()} />
          )}
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}

const roundButton =
  'flex size-11 items-center justify-center rounded-full outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus';

function CloseButton({ close }: { close: () => void }) {
  const { t } = useTranslation();
  return (
    <Button aria-label={t('story.close')} onPress={close} className={roundButton}>
      <X aria-hidden className="size-6" />
    </Button>
  );
}

const pauseIcons = { 'story.play': Play, 'story.pause': Pause };

interface PauseButtonProps {
  label: keyof typeof pauseIcons;
  toggle: () => void;
}

function PauseButton({ label, toggle }: PauseButtonProps) {
  const { t } = useTranslation();
  const Icon = pauseIcons[label];
  return (
    <Button ref={focusOnMount} aria-label={t(label)} onPress={toggle} className={roundButton}>
      <Icon aria-hidden className="size-6" />
    </Button>
  );
}

interface FallbackProps {
  state: DayView;
  close: () => void;
  retry: () => void;
}

function Fallback({ state, close, retry }: FallbackProps) {
  return (
    <>
      {state.empty && <Navigate to="/" replace />}
      {state.failure && (
        <div className="p-4 pt-16">
          <Failed message={state.failure} onRetry={retry} />
        </div>
      )}
      <div className="absolute top-[calc(env(safe-area-inset-top)+0.75rem)] right-2">
        <CloseButton close={close} />
      </div>
    </>
  );
}

interface PlayerProps {
  date: string;
  year: number;
  day: Day;
  close: () => void;
}

function Player({ date, year, day, close }: PlayerProps) {
  const { t } = useTranslation();
  const story = useStory(day.years, year, close);
  const {
    state,
    group,
    memory,
    segments,
    name,
    pause,
    controls,
    blurhash,
    failed,
    image,
    video,
    duration,
    sound,
    mediaReady,
    mediaFailed,
    gestures,
    videoEvents,
    nextVideo,
    togglePause,
    toggleSound,
  } = story;
  const interactions = useInteractions(date, story);
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
          <div
            key={state.visit}
            role="img"
            aria-label={t('story.memory', { ...name, kind: t(name.kind) })}
            className="absolute inset-0"
          >
            {blurhash && <Blurhash {...blurhash} />}
            <Picture
              image={image}
              src={memory.proxies.preview}
              onDecoded={mediaReady}
              onFailed={mediaFailed}
            />
          </div>
          <video
            {...videoEvents}
            hidden={!video}
            aria-hidden
            muted
            playsInline
            className="absolute inset-0 size-full object-contain"
          />
          {nextVideo && (
            <video key={nextVideo} src={nextVideo} preload="auto" hidden aria-hidden muted />
          )}
          {failed && (
            <p className="absolute inset-x-4 top-1/2 -translate-y-1/2 text-center font-semibold">
              {t('story.failed')}
            </p>
          )}
          <div className="absolute inset-0 flex">
            <Zone label={t('story.previous')} gestures={gestures} move="previous" />
            <Middle gestures={gestures} move="middle" />
            <Zone label={t('story.next')} gestures={gestures} move="next" />
          </div>
          <div className="pointer-events-none absolute inset-x-0 top-0 flex flex-col gap-1 bg-linear-to-b from-player/60 px-2 pt-[calc(env(safe-area-inset-top)+0.5rem)] pb-6">
            <Segments
              key={state.visit}
              count={segments.count}
              current={segments.current}
              duration={duration}
              running={state.running}
            />
            <div style={{ visibility: controls }} className="flex items-center">
              <p role="status" className="flex-1 text-small font-semibold">
                {t('onThisDay.yearsAgo', yearsAgo(date, group.year))}
              </p>
              <div className="pointer-events-auto flex">
                <PauseButton label={pause} toggle={togglePause} />
                <CloseButton close={close} />
              </div>
            </div>
          </div>
          <div style={{ visibility: controls }}>
            <MemoryActions
              interaction={interactionOf(day, memory)}
              onToggle={interactions.toggle}
              onHide={interactions.hide}
            />
            <Sound {...sound} toggle={toggleSound} />
          </div>
        </m.div>
        <Toasts toasts={interactions.toasts} />
      </MotionConfig>
    </LazyMotion>
  );
}

interface PictureProps {
  image: 'picture' | 'poster' | null;
  src: string | null;
  onDecoded: () => void;
  onFailed: () => void;
}

function Picture({ image, src, onDecoded, onFailed }: PictureProps) {
  const className = 'relative size-full object-contain';
  if (image === 'poster') return <ProxyImage src={src} className={className} />;
  return (
    image && (
      <ProxyImage src={src} onDecoded={onDecoded} onFailed={onFailed} className={className} />
    )
  );
}

interface BlurhashProps {
  hash: string;
  width: number;
  height: number;
}

function Blurhash({ hash, width, height }: BlurhashProps) {
  return (
    <canvas
      ref={(canvas) => {
        paintBlurhash(canvas, hash);
      }}
      width={width}
      height={height}
      className="absolute inset-0 size-full object-contain"
    />
  );
}

const soundIcons = { 'story.soundOn': VolumeX, 'story.soundOff': Volume2 };

interface SoundProps {
  shown: boolean;
  hint: boolean;
  label: keyof typeof soundIcons;
  toggle: () => void;
}

function Sound({ shown, hint, label, toggle }: SoundProps) {
  const { t } = useTranslation();
  const Icon = soundIcons[label];
  if (!shown) return null;
  return (
    <m.div
      layout
      data-hint={hint}
      className="group absolute right-2 bottom-[calc(env(safe-area-inset-bottom)+1.25rem)] size-11 data-[hint=true]:inset-0 data-[hint=true]:m-auto data-[hint=true]:size-32"
    >
      <Button
        aria-label={t(label)}
        onPress={toggle}
        className="flex size-full items-center justify-center rounded-full bg-player/40 outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
      >
        <Icon aria-hidden className="size-1/2 group-data-[hint=true]:opacity-60" />
      </Button>
    </m.div>
  );
}

interface PressZoneProps {
  gestures: Gestures;
  move: Tap;
}

interface ZoneProps extends PressZoneProps {
  move: Move;
  label: string;
}

function Zone({ label, gestures, move }: ZoneProps) {
  return (
    <button
      type="button"
      aria-label={label}
      {...useZonePress(gestures, move)}
      className="h-full flex-1 outline-none focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-focus"
    />
  );
}

function Middle({ gestures, move }: PressZoneProps) {
  return <div {...useZonePress(gestures, move)} aria-hidden className="h-full flex-1" />;
}

interface SegmentsProps {
  count: number;
  current: number;
  duration: number;
  running: boolean;
}

function Segments({ count, current, duration, running }: SegmentsProps) {
  return (
    <ol aria-hidden className="flex gap-1">
      {Array.from({ length: count }, (_, index) => (
        <li key={index} className="h-0.5 flex-1 overflow-hidden rounded-full bg-on-player/40">
          <span
            style={{
              animationDuration: `${String(duration)}ms`,
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
