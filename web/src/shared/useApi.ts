import { useCallback, useEffect, useState } from 'react';
import { apiFetch, type ApiResult } from './api';
import { useSession } from '../app/MeContext';

/** Bound API calls for the current session. */
export function useApi() {
  const { token } = useSession();
  const get = useCallback(<T,>(path: string) => apiFetch<T>(token, path), [token]);
  const post = useCallback(<T,>(path: string, body?: unknown) => apiFetch<T>(token, path, { method: 'POST', body }), [token]);
  return { get, post };
}

/** Loads a GET resource; `reload()` refetches. */
export function useResource<T>(path: string | null) {
  const { get } = useApi();
  const [state, setState] = useState<ApiResult<T> | null>(null);
  const [version, setVersion] = useState(0);
  useEffect(() => {
    if (!path) return;
    let cancelled = false;
    void get<T>(path).then((r) => {
      if (!cancelled) setState(r);
    });
    return () => {
      cancelled = true;
    };
  }, [get, path, version]);
  const reload = useCallback(() => setVersion((v) => v + 1), []);
  return { result: state, reload };
}
