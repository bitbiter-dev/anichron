import { Link, type LinkProps } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import { Button } from 'react-aria-components';

export const rowStyles =
  'flex min-h-11 w-full items-center px-4 py-3 font-semibold outline-none focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-focus';

export const rowLinkStyles = `${rowStyles} justify-between gap-3`;

export function RowChevron() {
  return <ChevronRight aria-hidden className="size-5 text-fg-muted" />;
}

interface RowLinkProps {
  to: LinkProps['to'];
  children: string;
}

export function RowLink({ to, children }: RowLinkProps) {
  return (
    <Link to={to} className={rowLinkStyles}>
      {children}
      <RowChevron />
    </Link>
  );
}

interface RowButtonProps {
  onPress: () => void;
  children: string;
}

export function RowButton({ onPress, children }: RowButtonProps) {
  return (
    <Button onPress={onPress} className={`${rowStyles} justify-center text-danger`}>
      {children}
    </Button>
  );
}
