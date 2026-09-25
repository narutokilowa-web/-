import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { expect, test } from '@playwright/test';
import { password, signIn } from './helpers';

// M0 acceptance (kept as a regression test): every seeded user can sign in through Keycloak and sees
// their role(s), which the API reads from OUR database (not from the token).

interface DevUser {
  id: string;
  username: string;
  roles: string[];
}

const devUsersPath = fileURLToPath(new URL('../../deploy/seed/dev-users.json', import.meta.url));
const devUsers = (JSON.parse(readFileSync(devUsersPath, 'utf8')) as { users: DevUser[] }).users;

for (const user of devUsers) {
  test(`${user.username} signs in and sees roles ${user.roles.join(',')} from the database`, async ({ page }) => {
    await signIn(page, user.username);
    for (const role of user.roles) {
      await expect(page.locator(`[data-role="${role}"]`)).toBeVisible();
    }
    await expect(page.locator('[data-role]')).toHaveCount(user.roles.length);
    await expect(page.getByTestId('roles-source')).toBeVisible();
  });
}

test('a wrong password is rejected by Keycloak', async ({ page }) => {
  await page.goto('/');
  await page.getByTestId('login').click();
  await page.locator('#username').fill(devUsers[0]!.username);
  await page.locator('#password').fill(password() + '-wrong');
  await page.locator('#kc-login').click();
  await expect(page.locator('#kc-login')).toBeVisible();
  await expect(page.getByTestId('me')).toHaveCount(0);
});
