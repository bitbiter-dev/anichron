import { useId } from 'react';
import { Button } from 'react-aria-components';
import { useTranslation } from 'react-i18next';
import type { HiddenMemory } from '../../api/hidden';
import { useBlurhash } from '../../shared/use-blurhash';
import { Blurhash } from '../../shared/ui/BlurhashCanvas';
import { Failed } from '../../shared/ui/Failed';
import { LivePhotoIcon } from '../../shared/ui/LivePhotoIcon';
import { ProxyImage } from '../../shared/ui/ProxyImage';
import { Skeleton } from '../../shared/ui/Skeleton';
import { monthFormat, type Section, takenFormat, tileOf } from './hidden';
import { HiddenViewer } from './HiddenViewer';
import { type HiddenScreen, useHidden } from './use-hidden';

const grid = 'grid grid-cols-3 gap-1 sm:grid-cols-4 lg:grid-cols-6';

export function HiddenPage() {
  const screen = useHidden();
  return (
    <main aria-busy={screen.loading} className="flex flex-col gap-6 p-4">
      {screen.loading && <GridSkeleton />}
      <Sections screen={screen} />
      {screen.failure && <Failed message={screen.failure} onRetry={screen.retry} />}
      <HiddenViewer screen={screen} />
    </main>
  );
}

function GridSkeleton() {
  return (
    <div aria-hidden className="flex flex-col gap-2">
      <Skeleton className="h-4 w-24 rounded-full" />
      <div className={grid}>
        {[0, 1, 2, 3, 4, 5].map((tile) => (
          <Skeleton key={tile} className="rounded-sm aspect-square" />
        ))}
      </div>
    </div>
  );
}

function Sections({ screen }: { screen: HiddenScreen }) {
  const { t } = useTranslation();
  if (screen.empty)
    return (
      <div className="flex flex-col gap-1">
        <p className="font-semibold">{t('hidden.empty')}</p>
        <p className="text-small text-fg-muted">{t('hidden.emptyHint')}</p>
      </div>
    );
  return screen.sections.map((section) => (
    <SectionView key={section.id} section={section} open={screen.open} />
  ));
}

interface SectionViewProps {
  section: Section;
  open: HiddenScreen['open'];
}

function SectionView({ section, open }: SectionViewProps) {
  const { t } = useTranslation();
  const id = useId();
  return (
    <section aria-labelledby={id} className="flex flex-col gap-2">
      <h2 id={id} className="font-semibold">
        {t(section.heading.key, { ...section.heading, formatParams: monthFormat() })}
      </h2>
      <ul className={grid}>
        {section.items.map(({ item, index }) => (
          <li key={item.memory.id}>
            <Tile item={item} onPress={open(index)} />
          </li>
        ))}
      </ul>
    </section>
  );
}

interface TileProps {
  item: HiddenMemory;
  onPress: () => void;
}

function Tile({ item: { memory }, onPress }: TileProps) {
  const { t } = useTranslation();
  const { blurhash, onDecoded } = useBlurhash(memory);
  const tile = tileOf(memory);
  return (
    <Button
      aria-label={t('hidden.memory', {
        kind: t(tile.name),
        taken: tile.taken,
        formatParams: takenFormat(),
      })}
      onPress={onPress}
      className="rounded-sm relative block aspect-square w-full overflow-hidden bg-surface-raised outline-none focus-visible:focus-ring"
    >
      {blurhash && <Blurhash {...blurhash} />}
      <ProxyImage
        src={memory.proxies.thumbnail}
        onDecoded={onDecoded}
        className="relative size-full object-cover"
      />
      {tile.live && (
        <LivePhotoIcon className="absolute top-1 right-1 size-5 rounded-full p-0.5 player-glass" />
      )}
    </Button>
  );
}
