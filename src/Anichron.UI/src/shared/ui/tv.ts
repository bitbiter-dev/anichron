import { createTV } from 'tailwind-variants';

export const tv = createTV({
  twMergeConfig: {
    extend: {
      theme: {
        text: ['small', 'body', 'title'],
        radius: ['field', 'card'],
      },
    },
  },
});
