import { useRouter } from '@tanstack/react-router';
import { z } from 'zod/mini';
import type { ValidationCode } from '../../api/problem';
import { useProblemForm } from '../../shared/use-problem-form';
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

export type PasswordForm = ReturnType<typeof usePasswordForm>;

export function usePasswordForm(afterChange: 'home' | 'requested') {
  const router = useRouter();
  const { api, toasts } = router.options.context;
  return useProblemForm({
    schema: passwordSchema(),
    defaultValues: { currentPassword: '', newPassword: '', confirmPassword: '' },
    messages: passwordMessages(),
    sentFields: ['currentPassword', 'newPassword'],
    submit: async ({ currentPassword, newPassword }) => {
      await api.session.changePassword({ currentPassword, newPassword });
      toasts.show({ message: 'password.changed' });
      if (afterChange === 'home') await router.navigate({ to: '/', replace: true });
    },
  });
}
