import { EyeOff, Heart, Star, type LucideIcon } from 'lucide-react';
import { Button, ToggleButton } from 'react-aria-components';
import { useTranslation } from 'react-i18next';
import type { Memory } from '../../api/memories';
import type { Toggle } from './use-interactions';

const controlStyles =
  'group flex size-11 items-center justify-center rounded-full outline-none focus-visible:focus-ring';

interface MemoryActionsProps {
  interaction: Memory['interaction'];
  onToggle: (flag: Toggle) => (on: boolean) => void;
  onHide: () => void;
}

export function MemoryActions({ interaction, onToggle, onHide }: MemoryActionsProps) {
  const { t } = useTranslation();
  return (
    <div
      data-floating="player"
      className="absolute inset-x-0 bottom-(--player-pill-bottom) mx-auto flex w-fit gap-2 rounded-full p-1 player-glass"
    >
      <ToggleAction
        flag="starred"
        icon={Star}
        label={t('story.star')}
        interaction={interaction}
        onToggle={onToggle}
      />
      <ToggleAction
        flag="liked"
        icon={Heart}
        label={t('story.like')}
        interaction={interaction}
        onToggle={onToggle}
      />
      <Button aria-label={t('story.hide')} onPress={onHide} className={controlStyles}>
        <EyeOff aria-hidden className="size-6" />
      </Button>
    </div>
  );
}

interface ToggleActionProps extends Omit<MemoryActionsProps, 'onHide'> {
  flag: Toggle;
  icon: LucideIcon;
  label: string;
}

function ToggleAction({ flag, icon: Icon, label, interaction, onToggle }: ToggleActionProps) {
  return (
    <ToggleButton
      aria-label={label}
      isSelected={interaction[flag]}
      onChange={onToggle(flag)}
      className={controlStyles}
    >
      <Icon aria-hidden className="size-6 group-data-selected:fill-current" />
    </ToggleButton>
  );
}
