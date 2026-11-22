import { fireEvent, screen } from '@testing-library/react';

type Queries = Pick<typeof screen, 'getByLabelText' | 'getByRole'>;

export function signIn(usernameOrEmail: string, on: Queries = screen) {
  fireEvent.change(on.getByLabelText('Username or email'), { target: { value: usernameOrEmail } });
  fireEvent.change(on.getByLabelText('Password'), { target: { value: 'hunter2' } });
  fireEvent.click(on.getByRole('button', { name: 'Sign in' }));
}
