import { useTranslation } from 'react-i18next';

export function RoleList({ roles }: { roles: string[] }) {
  const { t } = useTranslation();
  if (roles.length === 0) {
    return <p data-testid="no-roles">{t('me.noRoles')}</p>;
  }
  return (
    <ul data-testid="roles">
      {roles.map((role) => (
        <li key={role} data-role={role}>
          {t(`roles.${role}`, { defaultValue: role })} <code>{role}</code>
        </li>
      ))}
    </ul>
  );
}
