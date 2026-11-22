import { useRouter } from '@tanstack/react-router';
import { z } from 'zod/mini';
import type { ValidationCode } from '../../api/problem';
import { useProblemForm } from '../../shared/use-problem-form';
import { loginMessages } from './messages';

function loginSchema() {
  const required = z.minLength(1, { error: 'required' satisfies ValidationCode });
  return z.object({
    usernameOrEmail: z.string().check(z.trim(), required),
    password: z.string().check(required),
  });
}

export function useLoginForm() {
  const router = useRouter();
  const { api } = router.options.context;
  return useProblemForm({
    schema: loginSchema(),
    defaultValues: { usernameOrEmail: '', password: '' },
    messages: loginMessages(),
    submit: async (values) => {
      await api.session.login(values);
      await router.invalidate();
    },
  });
}
