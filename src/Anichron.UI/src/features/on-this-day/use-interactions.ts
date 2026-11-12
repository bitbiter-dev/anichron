import { useMutation } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { useState } from 'react';
import type { Change } from '../../api/assets';
import type { Memory } from '../../api/flashbacks';
import { createToasts } from '../../shared/toasts';
import { interactionMutation } from './day';
import type { Place } from './story';

export type Toggle = 'starred' | 'liked';

interface Hiding {
  memory: Memory;
  memoryAt: (place: Place) => Memory;
  hide: (place?: Place) => Place;
  undo: (place: Place) => void;
}

export function useInteractions(date: string, story: Hiding) {
  const { api, queryClient } = useRouter().options.context;
  const [toasts] = useState(createToasts);
  const { mutateAsync } = useMutation(interactionMutation(api, queryClient, date));

  function send(memory: Memory, change: Change, rollBack: () => void = () => undefined) {
    mutateAsync({ id: memory.id, change }).catch(() => {
      rollBack();
      toasts.show({ message: 'story.notSaved' });
    });
  }

  function undo(place: Place) {
    story.undo(place);
    send(story.memoryAt(place), { hidden: false }, () => story.hide(place));
  }

  return {
    toasts,
    toggle: (flag: Toggle) => (on: boolean) => {
      send(story.memory, { [flag]: on });
    },
    hide: () => {
      const { memory } = story;
      const place = story.hide();
      const key = toasts.show({
        message: 'story.hidden',
        action: {
          label: 'story.undo',
          onAction: () => {
            undo(place);
          },
        },
      });
      send(memory, { hidden: true }, () => {
        toasts.close(key);
        story.undo(place);
      });
    },
  };
}
