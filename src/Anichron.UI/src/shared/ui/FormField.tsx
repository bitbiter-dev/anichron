import { Controller, type Control, type FieldValues, type Path } from 'react-hook-form';
import { TextField, type TextFieldProps } from './TextField';

interface FormFieldProps<F extends FieldValues> extends Omit<
  TextFieldProps,
  'name' | 'value' | 'onChange' | 'onBlur' | 'inputRef'
> {
  control: Control<F>;
  name: Path<F>;
}

export function FormField<F extends FieldValues>({ control, name, ...props }: FormFieldProps<F>) {
  return (
    <Controller
      control={control}
      name={name}
      render={({ field }) => (
        <TextField
          {...props}
          name={field.name}
          value={field.value}
          onChange={field.onChange}
          onBlur={field.onBlur}
          inputRef={field.ref}
        />
      )}
    />
  );
}
