import { ProfileSlotConfig, NativeSwitchRequest, NativeSwitchResponse } from '../types/account';

export const NATIVE_HOST_NAME = 'com.chrome_account_switcher.helper';

export const storageService = {
  /**
   * Retrieves the map of ProfileDirectory -> Shortcut.
   * Migrates legacy slot-based shortcuts if necessary.
   */
  getProfileShortcuts: async (): Promise<Record<string, string>> => {
    const res = await chrome.storage.local.get(['profileShortcuts', 'profileSlots']);
    let map: Record<string, string> = res.profileShortcuts || {};

    // Migration: If profileShortcuts is empty, migrate from existing profileSlots
    if (Object.keys(map).length === 0 && res.profileSlots && Array.isArray(res.profileSlots)) {
      map = {};
      for (const s of res.profileSlots as ProfileSlotConfig[]) {
        if (s.profileDirectory && s.shortcut) {
          map[s.profileDirectory] = s.shortcut;
        }
      }
      if (Object.keys(map).length > 0) {
        await chrome.storage.local.set({ profileShortcuts: map });
      }
    }

    return map;
  },

  /**
   * Saves a custom shortcut for a specific profile directory.
   */
  setProfileShortcut: async (directory: string, shortcut?: string): Promise<Record<string, string>> => {
    const map = await storageService.getProfileShortcuts();
    if (shortcut && shortcut.trim()) {
      map[directory] = shortcut.trim();
    } else {
      delete map[directory];
    }
    await chrome.storage.local.set({ profileShortcuts: map });
    return map;
  },

  /**
   * Fetches dynamically discovered profiles from native helper,
   * attaches directory-bound shortcuts, and assigns dynamic 1..N slot positions.
   */
  getSlotConfigs: async (): Promise<ProfileSlotConfig[]> => {
    const shortcuts = await storageService.getProfileShortcuts();

    try {
      const response = await sendNativeMessage({ action: 'getProfiles' });
      if (response.success && response.profiles && response.profiles.length > 0) {
        const dynamicSlots: ProfileSlotConfig[] = response.profiles.map((p, idx) => {
          const slotNum = idx + 1;
          const assignedShortcut = shortcuts[p.directory] || (slotNum <= 4 ? `Alt + Shift + ${slotNum}` : undefined);
          return {
            slot: slotNum,
            profileDirectory: p.directory,
            displayName: p.displayName || p.directory,
            gaiaName: p.gaiaName,
            email: p.email,
            avatarIcon: p.avatarIcon,
            shortcut: assignedShortcut,
            isCurrent: !!p.isCurrent
          };
        });

        await chrome.storage.local.set({ profileSlots: dynamicSlots });
        return dynamicSlots;
      }
    } catch (err) {
      console.warn('[Storage] Failed to query dynamic profiles from helper:', err);
    }

    // Fallback: Return previously cached slots from local storage
    const cached = await chrome.storage.local.get('profileSlots');
    const existing = cached.profileSlots as ProfileSlotConfig[] | undefined;
    if (existing && Array.isArray(existing) && existing.length > 0) {
      return existing.map((s, idx) => ({
        ...s,
        slot: idx + 1,
        shortcut: shortcuts[s.profileDirectory] || s.shortcut
      }));
    }

    // Minimal fallback if helper never connected
    return [
      { slot: 1, profileDirectory: 'Default', displayName: 'Default', shortcut: shortcuts['Default'] || 'Alt + Shift + 1' },
      { slot: 2, profileDirectory: 'Profile 1', displayName: 'Profile 1', shortcut: shortcuts['Profile 1'] || 'Alt + Shift + 2' }
    ];
  },

  setSlotConfigs: async (slots: ProfileSlotConfig[]): Promise<void> => {
    await chrome.storage.local.set({ profileSlots: slots });
  },

  updateSlotShortcut: async (slotNumber: number, shortcut?: string): Promise<ProfileSlotConfig[]> => {
    const slots = await storageService.getSlotConfigs();
    const targetSlot = slots.find((s) => s.slot === slotNumber);
    if (targetSlot) {
      await storageService.setProfileShortcut(targetSlot.profileDirectory, shortcut);
    }
    return await storageService.getSlotConfigs();
  },

  updateProfileShortcutByDirectory: async (directory: string, shortcut?: string): Promise<ProfileSlotConfig[]> => {
    await storageService.setProfileShortcut(directory, shortcut);
    return await storageService.getSlotConfigs();
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
      console.log(`[Diagnostic] NATIVE MESSAGE CONNECT/SEND START: host='${NATIVE_HOST_NAME}', extId='${chrome.runtime.id}'`);
      chrome.runtime.sendNativeMessage(
        NATIVE_HOST_NAME,
        message,
        (response: NativeSwitchResponse | undefined) => {
          if (chrome.runtime.lastError) {
            const err = chrome.runtime.lastError.message || 'Failed to communicate with native helper.';
            console.error(`[Diagnostic] NATIVE MESSAGE ERROR: ${err}`);
            resolve({
              success: false,
              error: err
            });
            return;
          }

          if (!response) {
            console.error('[Diagnostic] NATIVE MESSAGE ERROR: Empty response received from native helper.');
            resolve({
              success: false,
              error: 'Empty response received from native helper.'
            });
            return;
          }

          console.log('[Diagnostic] NATIVE MESSAGE SUCCESS:', response);
          resolve(response);
        }
      );
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err);
      console.error(`[Diagnostic] NATIVE MESSAGE EXCEPTION: ${errMsg}`);
      resolve({
        success: false,
        error: errMsg
      });
    }
  });
};
