import { extendTailwindMerge } from 'tailwind-merge';
import { createTV } from 'tailwind-variants';
import tokens from './tokens.css?raw';

function scale(namespace: string) {
  return Array.from(
    tokens.matchAll(new RegExp(`(?<=--${namespace}-)[a-z]+(?=:)`, 'g')),
    ([name]) => name,
  );
}

export const tv = createTV({
  twMerge: extendTailwindMerge({
    extend: {
      theme: {
        text: scale('text'),
        radius: scale('radius'),
      },
    },
  }),
});
