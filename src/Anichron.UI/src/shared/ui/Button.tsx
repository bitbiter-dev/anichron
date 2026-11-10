import { Button as AriaButton, type ButtonProps as AriaButtonProps } from 'react-aria-components';
import { tv } from './tv';

export const buttonStyles = tv({
  base: 'inline-flex min-h-11 items-center justify-center rounded-full px-6 font-semibold transition-opacity',
  variants: {
    variant: {
      primary: 'bg-accent text-on-accent lift-accent',
      danger: 'text-danger',
    },
    isHovered: { true: 'opacity-90' },
    isPressed: { true: 'opacity-80' },
    isFocusVisible: { true: 'outline-2 outline-offset-2 outline-focus', false: 'outline-none' },
    isDisabled: { true: 'opacity-50' },
  },
  defaultVariants: { variant: 'primary' },
});

interface ButtonProps extends Omit<AriaButtonProps, 'className'> {
  variant?: 'primary' | 'danger';
  className?: string;
}

export function Button({ variant, className, ...props }: ButtonProps) {
  return (
    <AriaButton
      {...props}
      className={(state) => buttonStyles({ ...state, variant, class: className })}
    />
  );
}
