import type { ParseKeys } from 'i18next';
import type { Ref } from 'react';
import { useTranslation } from 'react-i18next';
import {
  FieldError,
  Input,
  Label,
  TextField as AriaTextField,
  type TextFieldProps as AriaTextFieldProps,
} from 'react-aria-components';
import { tv } from './tv';

export const inputStyles = tv({
  base: 'min-h-11 rounded-field border border-border bg-surface px-3 text-fg',
  variants: {
    isHovered: { true: 'border-fg-muted' },
    isFocusVisible: { true: 'outline-2 outline-offset-2 outline-focus', false: 'outline-none' },
    isInvalid: { true: 'border-danger' },
    isDisabled: { true: 'opacity-50' },
  },
});

const fieldStyles = tv({ base: 'flex flex-col gap-1.5' });

interface TextFieldProps extends Omit<
  AriaTextFieldProps,
  'className' | 'children' | 'isInvalid' | 'validationBehavior' | 'validate'
> {
  label: string;
  error?: ParseKeys | undefined;
  inputRef?: Ref<HTMLInputElement>;
  className?: string;
}

export function TextField({ label, error, inputRef, className, ...props }: TextFieldProps) {
  const { t } = useTranslation();
  const isInvalid = error !== undefined;
  return (
    <AriaTextField
      {...props}
      isInvalid={isInvalid}
      validationBehavior="aria"
      className={fieldStyles({ class: className })}
    >
      <Label className="text-small font-medium">{label}</Label>
      <Input ref={inputRef} aria-invalid={isInvalid} className={(state) => inputStyles(state)} />
      <FieldError className="text-small text-danger">{error && t(error)}</FieldError>
    </AriaTextField>
  );
}
