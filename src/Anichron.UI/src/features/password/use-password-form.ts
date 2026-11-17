import { zodResolver } from '@hookform/resolvers/zod';
import { useRouter } from '@tanstack/react-router';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod/mini';
import { fieldCodes, problemOf, type ValidationCode } from '../../api/problem';
import { useCountdown } from '../../shared/use-countdown';
import { messageKey } from '../../shared/messages';
import { passwordMessages } from './messages';

function passwordSchema() {
  const required = z.minLength(1, { error: 'required' satisfies ValidationCode });
  return z
    .object({
      currentPassword: z.string().check(required),
      newPassword: z.string().check(required),
      confirmPassword: z.string().check(required),
    })
    .check(
      z.refine(({ newPassword, confirmPassword }) => confirmPassword === newPassword, {
        error: 'mismatch' satisfies ValidationCode,
        path: ['confirmPassword'],
      }),
    );
}

type PasswordFields = z.infer<ReturnType<typeof passwordSchema>>;

function sentFields() {
  return ['currentPassword', 'newPassword'] as const;
}

export type PasswordForm = ReturnType<typeof usePasswordForm>;

export function usePasswordForm() {
  const { api } = useRouter().options.context;
  const countdown = useCountdown();
  const [changed, setChanged] = useState(false);
  const { control, handleSubmit, setError, reset, formState } = useForm<PasswordFields>({
    resolver: zodResolver(passwordSchema()),
    defaultValues: { currentPassword: '', newPassword: '', confirmPassword: '' },
  });

  const onSubmit = handleSubmit(async ({ currentPassword, newPassword }) => {
    setChanged(false);
    const problem = await api.session
      .changePassword({ currentPassword, newPassword })
      .then(() => undefined, problemOf);
    if (!problem) {
      reset();
      setChanged(true);
      return;
    }
    const onFields = fieldCodes(problem, sentFields());
    for (const [field, code] of onFields) setError(field, { message: code });
    if (onFields.length === 0) setError('root', { message: problem.code });
    countdown.start(problem.retryAfterSeconds);
  });

  const { errors, isSubmitting } = formState;
  return {
    control,
    onSubmit,
    changed,
    formError: errors.root && messageKey(errors.root.message, passwordMessages()),
    fieldError: (field: keyof PasswordFields) =>
      errors[field] && messageKey(errors[field].message, passwordMessages()),
    retryIn: countdown.remaining,
    blocked: isSubmitting || countdown.remaining > 0,
  };
}
