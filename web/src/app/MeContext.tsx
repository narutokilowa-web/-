import { createContext, useContext } from 'react';
import type { CurrentUser } from '../shared/api';

export interface Session {
  token: string;
  me: CurrentUser;
}

export const SessionContext = createContext<Session | null>(null);

export function useSession(): Session {
  const session = useContext(SessionContext);
  if (!session) throw new Error('useSession outside an authenticated session');
  return session;
}

export function hasRole(session: Session, ...roles: string[]): boolean {
  return roles.some((r) => session.me.roles.includes(r));
}
