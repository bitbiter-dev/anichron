import { zodResolver } from '@hookform/resolvers/zod';
import { useRouter } from '@tanstack/react-router';
import { useForm } from 'react-hook-form';
import { z } from 'zod/mini';
import { fieldCodes, type ErrorCode } from '../../api/problem';
import { useCountdown } from '../../shared/use-countdown';
import { messageKey } from './messages';

const required = z.minLength(1, { error: 'required' satisfies ErrorCode });

const loginSchema = z.object({
  usernameOrEmail: z.string().check(z.trim(), required),
  password: z.string().check(required),
});

export type LoginFields = z.infer<typeof loginSchema>;

const fields = ['usernameOrEmail', 'password'] as const;

export function useLoginForm() {
  const router = useRouter();
  const { api } = router.options.context;
  const countdown = useCountdown();
  const { control, handleSubmit, setError, formState } = useForm<LoginFields>({
    resolver: zodResolver(loginSchema),
    defaultValues: { usernameOrEmail: '', password: '' },
  });

  const onSubmit = handleSubmit(async (values) => {
    const outcome = await api.login(values);
    if (outcome.ok) {
      await router.invalidate();
      return;
    }
    setError('root', { message: outcome.problem.code });
    for (const [field, code] of fieldCodes(outcome.problem, fields))
      setError(field, { message: code });
    countdown.start(outcome.problem.retryAfterSeconds);
  });

  const { errors, isSubmitting } = formState;
  return {
    control,
    onSubmit,
    formError: errors.root && messageKey(errors.root.message),
    fieldError: (field: (typeof fields)[number]) =>
      errors[field] && messageKey(errors[field].message),
    retryIn: countdown.remaining,
    blocked: isSubmitting || countdown.remaining > 0,
  };
}
