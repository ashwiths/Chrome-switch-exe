import { ProfileSlotConfig, NativeSwitchRequest, NativeSwitchResponse } from '../types/account';

export const NATIVE_HOST_NAME = 'com.chrome_account_switcher.helper';

export const storageService = {
  getSlotConfigs: async (): Promise<ProfileSlotConfig[]> => {
    const res = await chrome.storage.local.get('profileSlots');
    return (res.profileSlots as ProfileSlotConfig[]) || [
      { slot: 1, profileDirectory: 'Default', displayName: 'Slot 1' },
      { slot: 2, profileDirectory: 'Profile 7', displayName: 'Slot 2' },
      { slot: 3, profileDirectory: 'Profile 2', displayName: 'Slot 3' },
      { slot: 4, profileDirectory: 'Profile 1', displayName: 'Slot 4' },
      { slot: 5, profileDirectory: 'Profile 3', displayName: 'Slot 5' }
    ];
  },

  setSlotConfigs: async (slots: ProfileSlotConfig[]): Promise<void> => {
    await chrome.storage.local.set({ profileSlots: slots });
  },

  getLastStatus: async (): Promise<{ message: string; timestamp: number } | null> => {
    const res = await chrome.storage.local.get('lastStatus');
    return res.lastStatus || null;
  },

  setLastStatus: async (message: string): Promise<void> => {
    await chrome.storage.local.set({
      lastStatus: { message, timestamp: Date.now() }
    });
  }
};

export const sendNativeMessage = (
  message: NativeSwitchRequest | { action: string; [key: string]: unknown }
): Promise<NativeSwitchResponse> => {
  return new Promise((resolve) => {
    try {
      chrome.runtime.sendNativeMessage(
        NATIVE_HOST_NAME,
        message,
        (response: NativeSwitchResponse | undefined) => {
          if (chrome.runtime.lastError) {
            resolve({
              success: false,
              error: chrome.runtime.lastError.message || 'Failed to communicate with native helper.'
            });
            return;
          }

          if (!response) {
            resolve({
              success: false,
              error: 'Empty response received from native helper.'
            });
            return;
          }

          resolve(response);
        }
      );
    } catch (err: unknown) {
      resolve({
        success: false,
        error: err instanceof Error ? err.message : String(err)
      });
    }
  });
};
