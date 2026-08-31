// Storage service placeholder
export const storageService = {
  get: async <T>(key: string): Promise<T | null> => {
    const result = await chrome.storage.local.get(key);
    return (result[key] as T) ?? null;
  },
  set: async <T>(key: string, value: T): Promise<void> => {
    await chrome.storage.local.set({ [key]: value });
  }
};
