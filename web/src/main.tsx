import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { AuthProvider } from 'react-oidc-context';
import { WebStorageStateStore } from 'oidc-client-ts';
import './shared/i18n';
import { applyDocumentLanguage } from './shared/i18n';
import { loadConfig } from './shared/config';
import { App } from './app/App';

applyDocumentLanguage('ar');

const config = await loadConfig();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AuthProvider
      authority={config.oidcAuthority}
      client_id={config.oidcClientId}
      redirect_uri={window.location.origin + '/'}
      post_logout_redirect_uri={window.location.origin + '/'}
      scope="openid"
      // Tokens stay in session storage only (never localStorage) and are cleared when the tab closes.
      userStore={new WebStorageStateStore({ store: window.sessionStorage })}
      onSigninCallback={() => window.history.replaceState({}, document.title, window.location.pathname)}
    >
      <App environment={config.environment} />
    </AuthProvider>
  </StrictMode>,
);
