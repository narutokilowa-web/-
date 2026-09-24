import { render, screen } from '@testing-library/react';
import { describe, expect, it, beforeEach } from 'vitest';
import i18n from '../shared/i18n';
import { RoleList } from './RoleList';

describe('RoleList', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('ar');
  });

  it('renders every role with its Arabic name and code', () => {
    render(<RoleList roles={['REGISTRAR', 'APPROVER']} />);
    const items = screen.getAllByRole('listitem');
    expect(items).toHaveLength(2);
    expect(items[0]).toHaveTextContent('مسجّل أصول');
    expect(items[0]).toHaveAttribute('data-role', 'REGISTRAR');
    expect(items[1]).toHaveTextContent('معتمد أصول');
  });

  it('says so when there are no active roles', () => {
    render(<RoleList roles={[]} />);
    expect(screen.getByTestId('no-roles')).toBeInTheDocument();
  });
});
