import { useMutation } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { useState } from 'react';
import type { Change, Flag } from '../../api/assets';
import type { Memory } from '../../api/flashbacks';
import { createToasts } from '../../shared/toasts';
import { otherFrames } from './burst';
import { burstQuery, interactionMutation } from './day';
import type { Place } from './story';

export type Toggle = Exclude<Flag, 'hidden'>;

interface StoryControls {
  memory: Memory;
  hide: (place?: Place) => Place;
  undo: (place: Place) => void;
  closeDetails: () => void;
}

export function useInteractions(date: string, story: StoryControls) {
  const { api, queryClient } = useRouter().options.context;
  const [toasts] = useState(createToasts);
  const { mutateAsync } = useMutation(interactionMutation(api, queryClient, date));

  function send(memory: Memory, change: Change, rollBack: () => void = () => undefined) {
    mutateAsync({ id: memory.id, change }).catch(() => {
      rollBack();
      toasts.show({ message: 'story.notSaved' });
    });
  }

  function hidden(onUndo: () => void) {
    return toasts.show({
      message: 'story.hidden',
      action: { label: 'story.undo', onAction: onUndo },
    });
  }

  function hideMemory(memory: Memory) {
    const place = story.hide();
    const key = hidden(() => {
      story.undo(place);
      send(memory, { hidden: false }, () => story.hide(place));
    });
    send(memory, { hidden: true }, () => {
      toasts.close(key);
      story.undo(place);
    });
  }

  function hideFrame(frame: Memory) {
    const key = hidden(() => {
      send(frame, { hidden: false });
    });
    send(frame, { hidden: true }, () => {
      toasts.close(key);
    });
  }

  function hide(memory: Memory) {
    const card = story.memory;
    if (otherFrames(card) < 1) {
      story.closeDetails();
      hideMemory(memory);
      return;
    }
    const burst = burstQuery(api, (card.burst as NonNullable<Memory['burst']>).burstId);
    void queryClient
      .query({ ...burst, staleTime: 'static' })
      .catch(() => undefined)
      .then(() => {
        hideFrame(memory);
      });
  }

  return {
    toasts,
    toggle:
      (flag: Toggle, memory = story.memory) =>
      (on: boolean) => {
        send(memory, { [flag]: on });
      },
    hide: (memory = story.memory) => {
      hide(memory);
    },
  };
}
