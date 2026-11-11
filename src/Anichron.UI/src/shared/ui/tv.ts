import { extendTailwindMerge } from 'tailwind-merge';
import { createTV } from 'tailwind-variants';

export const tv = createTV({
  twMerge: extendTailwindMerge({
    extend: {
      theme: {
        text: ['small', 'body', 'title'],
        radius: ['field', 'card'],
      },
    },
  }),
});
