import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate, useRouter } from '@tanstack/react-router';
import { useForm } from 'react-hook-form';
import { z } from 'zod/mini';
import { fieldCodes } from '../../api/problem';
import { useCountdown } from '../../shared/use-countdown';
import { messageKey } from './messages';

const required = z.minLength(1, { error: 'required' });

const loginSchema = z.object({
  usernameOrEmail: z.string().check(z.trim(), required),
  password: z.string().check(required),
});

type LoginFields = z.infer<typeof loginSchema>;

const fields = ['usernameOrEmail', 'password'] as const;

export function useLoginForm() {
  const { api } = useRouter().options.context;
  const navigate = useNavigate();
  const countdown = useCountdown();
  const { register, handleSubmit, setError, formState } = useForm<LoginFields>({
    resolver: zodResolver(loginSchema),
  });

  const onSubmit = handleSubmit(async (values) => {
    const outcome = await api.login(values);
    if (outcome.ok) {
      await navigate({ to: '/' });
      return;
    }
    setError('root', { message: outcome.problem.code });
    for (const [field, code] of fieldCodes(outcome.problem, fields))
      setError(field, { message: code });
    countdown.start(outcome.problem.retryAfterSeconds);
  });

  const { errors, isSubmitting } = formState;
  return {
    register,
    onSubmit,
    formError: errors.root && messageKey(errors.root.message),
    fieldError: (field: (typeof fields)[number]) =>
      errors[field] && messageKey(errors[field].message),
    retryIn: countdown.remaining,
    blocked: isSubmitting || countdown.remaining > 0,
  };
}
