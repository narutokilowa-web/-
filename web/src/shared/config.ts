/** Runtime configuration, loaded from /config.json so one built image works in every environment. */
export interface RuntimeConfig {
  oidcAuthority: string;
  oidcClientId: string;
  environment: string;
  /** Printed on QR labels. Configuration, not code: the pilot municipality is not decided yet. */
  organizationNameAr: string;
}

export async function loadConfig(): Promise<RuntimeConfig> {
  const response = await fetch('/config.json', { cache: 'no-store' });
  if (!response.ok) {
    throw new Error(`Cannot load /config.json (${response.status})`);
  }
  return (await response.json()) as RuntimeConfig;
}
