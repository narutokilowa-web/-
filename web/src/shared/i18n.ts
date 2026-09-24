import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import ar from '../locales/ar.json';
import en from '../locales/en.json';

export const supportedLanguages = ['ar', 'en'] as const;
export type Language = (typeof supportedLanguages)[number];

export function directionOf(lang: string): 'rtl' | 'ltr' {
  return lang === 'ar' ? 'rtl' : 'ltr';
}

export function applyDocumentLanguage(lang: string): void {
  document.documentElement.lang = lang;
  document.documentElement.dir = directionOf(lang);
}

void i18n.use(initReactI18next).init({
  resources: { ar: { translation: ar }, en: { translation: en } },
  lng: 'ar',
  fallbackLng: 'ar',
  interpolation: { escapeValue: false },
});

i18n.on('languageChanged', applyDocumentLanguage);

export default i18n;
