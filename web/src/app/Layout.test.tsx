import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { beforeEach, describe, expect, it } from 'vitest';
import i18n from '../shared/i18n';
import { SessionContext } from './MeContext';
import { Layout } from './Layout';

function renderAs(roles: string[]) {
  const me = { userId: 'u', username: 'user1', displayName: 'User', roles, rolesSource: 'database' as const };
  return render(
    <SessionContext.Provider value={{ token: 't', me }}>
      <MemoryRouter>
        <Layout environment="DEV"><p>content</p></Layout>
      </MemoryRouter>
    </SessionContext.Provider>,
  );
}

describe('Layout navigation follows roles from the database', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('ar');
  });

  it('shows "register light" only to registrars', () => {
    renderAs(['REGISTRAR']);
    expect(screen.getByTestId('nav-new-registration')).toBeInTheDocument();
  });

  it('hides registration creation from approvers', () => {
    renderAs(['APPROVER']);
    expect(screen.queryByTestId('nav-new-registration')).toBeNull();
    expect(screen.getByText('التسجيلات')).toBeInTheDocument();
  });

  it('shows no registration or asset links to a warehouse officer', () => {
    renderAs(['WAREHOUSE']);
    expect(screen.queryByText('التسجيلات')).toBeNull();
    expect(screen.queryByText('الأصول')).toBeNull();
  });
});
