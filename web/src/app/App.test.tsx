import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import i18n from '../shared/i18n';

const authState = vi.hoisted(() => ({
  value: { isLoading: false, isAuthenticated: false, error: undefined as Error | undefined, user: null as unknown, signinRedirect: vi.fn(), signoutRedirect: vi.fn() },
}));

vi.mock('react-oidc-context', () => ({ useAuth: () => authState.value }));

import { App } from './App';

describe('App', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('ar');
    authState.value = { ...authState.value, isAuthenticated: false, user: null };
  });

  it('shows the Arabic sign-in button when not authenticated', () => {
    render(<App environment="DEV" />);
    expect(screen.getByTestId('login')).toHaveTextContent('تسجيل الدخول');
    expect(screen.getByTestId('env-banner')).toHaveTextContent('DEV');
  });

  it('uses right-to-left layout for Arabic and left-to-right for English', async () => {
    render(<App environment="DEV" />);
    expect(document.documentElement.dir).toBe('rtl');
    await i18n.changeLanguage('en');
    expect(document.documentElement.dir).toBe('ltr');
    expect(document.documentElement.lang).toBe('en');
  });
});
