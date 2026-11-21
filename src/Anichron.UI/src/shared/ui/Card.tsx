import type { HTMLAttributes } from 'react';
import { tv } from './tv';

const cardStyles = tv({
  base: 'flex flex-col rounded-card glass-card',
  variants: { padded: { true: 'p-4', false: 'overflow-hidden' } },
  defaultVariants: { padded: true },
});

interface CardProps extends HTMLAttributes<HTMLElement> {
  as?: 'div' | 'section' | 'li';
  padded?: boolean;
}

export function Card({ as: Element = 'div', padded, className, ...props }: CardProps) {
  return <Element {...props} className={cardStyles({ padded, class: className })} />;
}
