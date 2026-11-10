import { Label, RadioButton, RadioField, RadioGroup } from 'react-aria-components';
import { tv } from './tv';

export const segmentStyles = tv({
  base: 'flex min-h-11 flex-1 cursor-default items-center justify-center rounded-full px-3 text-small font-medium text-fg-muted transition-colors',
  variants: {
    isHovered: { true: 'text-fg' },
    isSelected: { true: 'bg-accent text-on-accent lift-accent' },
    isFocusVisible: {
      true: 'outline-2 outline-offset-2 outline-focus transition-none',
      false: 'outline-none',
    },
    isDisabled: { true: 'opacity-50' },
  },
});

const groupStyles = tv({ base: 'flex flex-col gap-1.5' });

interface Option<T extends string> {
  value: T;
  label: string;
}

interface SegmentedControlProps<T extends string> {
  label: string;
  options: readonly Option<T>[];
  value: T;
  onChange: (value: T) => void;
  className?: string;
}

export function SegmentedControl<T extends string>({
  label,
  options,
  value,
  onChange,
  className,
}: SegmentedControlProps<T>) {
  return (
    <RadioGroup
      value={value}
      onChange={(chosen) => {
        onChange(chosen as T);
      }}
      orientation="horizontal"
      className={groupStyles({ class: className })}
    >
      <Label className="text-small font-medium">{label}</Label>
      <div className="flex gap-1 rounded-full border border-border p-1">
        {options.map((option) => (
          <RadioField key={option.value} value={option.value} className="flex flex-1">
            <RadioButton className={(state) => segmentStyles(state)}>{option.label}</RadioButton>
          </RadioField>
        ))}
      </div>
    </RadioGroup>
  );
}
