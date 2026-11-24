import { request, type FullConfig } from '@playwright/test';
import { admin, bootstrapAdmin, changePassword, createUser, mediaUser, signIn } from './accounts';
import { waitForIngestion } from './ingestion';

export default async function globalSetup(config: FullConfig) {
  const { baseURL, ignoreHTTPSErrors } = config.projects[0]?.use ?? {};
  const context = await request.newContext({ baseURL, ignoreHTTPSErrors });
  const response = await context.post('/api/v1/auth/login/mobile', {
    data: { usernameOrEmail: bootstrapAdmin.username, password: bootstrapAdmin.password },
  });
  if (response.status() !== 200)
    throw new Error(
      'The bootstrap admin cannot sign in with its first password, so the database is not fresh. ' +
        'Restart it: docker compose -f src/docker-compose.e2e.yml down, then up.',
    );
  const { accessToken } = (await response.json()) as { accessToken: string };
  await changePassword(context, accessToken, bootstrapAdmin.password, admin.password);
  const { storageConfigId } = await createUser(context, mediaUser);
  const adminToken = await signIn(context, admin.username, admin.password);
  await waitForIngestion(context, adminToken, storageConfigId);
  await context.dispose();
}
