import { useEffect, useState } from 'preact/hooks';

function readStored<T>(key: string, fallback: T): T {
  try {
    const raw = window.sessionStorage.getItem(key);
    if (raw === null) return fallback;
    return JSON.parse(raw) as T;
  } catch {
    return fallback;
  }
}

/**
 * State persisted in sessionStorage so list filters survive navigating into detail pages and back.
 * Re-reads automatically when `storageKey` changes (e.g. switching between asset categories).
 */
export function useSessionState<T>(storageKey: string, initialValue: T): [T, (next: T | ((prev: T) => T)) => void] {
  const [value, setValue] = useState<T>(() => readStored(storageKey, initialValue));

  useEffect(() => {
    setValue(readStored(storageKey, initialValue));
  }, [storageKey]);

  useEffect(() => {
    try {
      window.sessionStorage.setItem(storageKey, JSON.stringify(value));
    } catch {
      // Ignore storage quota or privacy-mode errors.
    }
  }, [storageKey, value]);

  return [value, setValue];
}
