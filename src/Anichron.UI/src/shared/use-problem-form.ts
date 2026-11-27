import { zodResolver } from '@hookform/resolvers/zod';
import { type DefaultValues, type Path, useForm } from 'react-hook-form';
import type { z } from 'zod/mini';
import { fieldCodes, problemOf } from '../api/problem';
import { messageKey, type Messages } from './messages';
import { useCountdown } from './use-countdown';

type Field<S extends z.ZodMiniObject> = keyof z.infer<S> & string;

interface ProblemFormOptions<S extends z.ZodMiniObject> {
  schema: S;
  defaultValues: DefaultValues<z.input<S>>;
  messages: Messages;
  submit: (values: z.infer<S>) => Promise<void>;
  sentFields?: readonly Field<S>[];
}

export function useProblemForm<S extends z.ZodMiniObject>({
  schema,
  defaultValues,
  messages,
  submit,
  sentFields = Object.keys(schema.shape),
}: ProblemFormOptions<S>) {
  const countdown = useCountdown();
  const { control, handleSubmit, setError, formState } = useForm({
    resolver: zodResolver(schema),
    defaultValues,
  });

  const onSubmit = handleSubmit(async (values) => {
    const problem = await submit(values).then(() => undefined, problemOf);
    if (!problem) return;
    const onFields = fieldCodes(problem, sentFields);
    for (const [field, code] of onFields) setError(field as Path<z.input<S>>, { message: code });
    if (onFields.length === 0) setError('root', { message: problem.code });
    countdown.start(problem.retryAfterSeconds);
  });

  const { errors, isSubmitting } = formState;
  return {
    control,
    onSubmit,
    formError: errors.root && messageKey(errors.root.message, messages),
    fieldError: (field: Field<S>) =>
      errors[field] && messageKey(errors[field].message as string | undefined, messages),
    retryIn: countdown.remaining,
    blocked: isSubmitting || countdown.remaining > 0,
  };
}
