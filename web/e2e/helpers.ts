import { expect, type Page } from '@playwright/test';

export const password = () => {
  const p = process.env.MFOP_DEV_USER_PASSWORD;
  if (!p) throw new Error('MFOP_DEV_USER_PASSWORD must be set (same value as in .env).');
  return p;
};

/** Signs in through Keycloak (Authorization Code + PKCE) from the current page. */
export async function signIn(page: Page, username: string, path = '/') {
  await page.goto(path);
  await page.getByTestId('login').click();
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password());
  await page.locator('#kc-login').click();
  await expect(page.getByTestId('username')).toHaveText(username);
}
