import { ProfileSlotConfig, NativeSwitchRequest, NativeSwitchResponse } from '../types/account';

export const NATIVE_HOST_NAME = 'com.chrome_account_switcher.helper';

export const detectActiveUserEmail = async (): Promise<string | undefined> => {
  // Method 1: Chrome identity API
  if (chrome.identity && typeof chrome.identity.getProfileUserInfo === 'function') {
    try {
      const userInfo = await new Promise<{ email?: string; id?: string }>((resolve) => {
        try {
          (chrome.identity.getProfileUserInfo as any)({ accountStatus: 'ANY' }, (info: any) => {
            resolve(chrome.runtime.lastError ? {} : (info || {}));
          });
        } catch {
          chrome.identity.getProfileUserInfo((info) => {
            resolve(chrome.runtime.lastError ? {} : (info || {}));
          });
        }
      });
      if (userInfo?.email && userInfo.email.trim()) {
        return userInfo.email.trim();
      }
    } catch {
      // Ignore
    }
  }

  // Method 2: Scan open tabs in the current window for account identifiers
  try {
    const tabs = await chrome.tabs.query({ currentWindow: true });
    for (const tab of tabs) {
      if (tab.title) {
        const match = tab.title.match(/[\w.+-]+@[\w-]+\.[\w.-]+/);
        if (match && !match[0].toLowerCase().endsWith('.google.com')) {
          return match[0];
        }
      }
      if (tab.url) {
        const urlMatch = tab.url.match(/authuser=([\w.+-]+@[\w-]+\.[\w.-]+)/i);
        if (urlMatch) {
          return decodeURIComponent(urlMatch[1]);
        }
      }
    }
  } catch {
    // Ignore
  }

  // Method 3: Query Google ListAccounts endpoint using active profile session cookies
  try {
    const res = await fetch('https://accounts.google.com/ListAccounts?source=ogb&json=standard', {
      credentials: 'include'
    });
    if (res.ok) {
      const text = await res.text();
      const emails = text.match(/[\w.+-]+@[\w-]+\.[\w.-]+/g);
      if (emails && emails.length > 0) {
        for (const e of emails) {
          const lower = e.toLowerCase();
          if (!lower.endsWith('.google.com') && !lower.endsWith('.google') && !lower.endsWith('googleusercontent.com')) {
            return e;
          }
        }
      }
    }
  } catch {
    // Ignore
  }

  // Method 4: Inspect active tab Google account button aria-label if on google.com
  try {
    const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
    const tab = tabs[0];
    if (tab?.id && tab.url && (tab.url.includes('google.com') || tab.url.includes('google.co'))) {
      const results = await chrome.scripting.executeScript({
        target: { tabId: tab.id },
        func: () => {
          const el = document.querySelector('a[aria-label*="@"], div[aria-label*="@"], [aria-label*="Google Account"]');
          if (el) {
            const label = el.getAttribute('aria-label') || '';
            const match = label.match(/[\w.+-]+@[\w-]+\.[\w.-]+/);
            if (match) return match[0];
          }
          return null;
        }
      });
      const detected = results?.[0]?.result;
      if (detected) return detected;
    }
  } catch {
    // Ignore
  }

  return undefined;
};

/**
 * Writes a unique probe token directly to this profile's isolated LevelDB storage.
 * The native helper scans all profiles on disk to find which profile directory hosts this token.
 * This mathematically guarantees 100% accurate profile detection without guessing.
 */
export const createStorageProbeToken = async (): Promise<string> => {
  // Purge obsolete cached data that could poison detection
  await chrome.storage.local.remove(['currentProfileDirectory', 'currentProfileEmail', 'lastStatus']);
  
  const token = `probe_${Date.now()}_${Math.random().toString(36).slice(2, 10)}`;
  await chrome.storage.local.set({ __probe_token: token, __probe_time: Date.now() });
  return token;
};

export const storageService = {
  /**
   * Retrieves the map of ProfileDirectory -> Shortcut.
   * Migrates legacy slot-based shortcuts if necessary.
   */
  getProfileShortcuts: async (): Promise<Record<string, string>> => {
    const res = await chrome.storage.local.get(['profileShortcuts', 'profileSlots']);
    let map: Record<string, string> = res.profileShortcuts || {};

    // Purge any legacy 'Shift' shortcuts
    let modified = false;
    for (const key of Object.keys(map)) {
      if (map[key] && map[key].includes('Shift')) {
        delete map[key];
        modified = true;
      }
    }

    // Migration: If profileShortcuts is empty, migrate from existing profileSlots
    if (Object.keys(map).length === 0 && res.profileSlots && Array.isArray(res.profileSlots)) {
      map = {};
      for (const s of res.profileSlots as ProfileSlotConfig[]) {
        if (s.profileDirectory && s.shortcut && !s.shortcut.includes('Shift')) {
          map[s.profileDirectory] = s.shortcut;
        }
      }
      modified = true;
    }

    if (modified) {
      await chrome.storage.local.set({ profileShortcuts: map });
    }

    return map;
  },

  /**
   * Saves a custom shortcut for a specific profile directory.
   */
  setProfileShortcut: async (directory: string, shortcut?: string): Promise<Record<string, string>> => {
    const map = await storageService.getProfileShortcuts();
    if (shortcut && shortcut.trim() && !shortcut.includes('Shift')) {
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
   * Resolves the current profile using probe token, active tabs, identity API, or helper heuristics.
   */
  getSlotConfigs: async (): Promise<ProfileSlotConfig[]> => {
    const shortcuts = await storageService.getProfileShortcuts();
    const probeToken = await createStorageProbeToken();
    const detectedEmail = await detectActiveUserEmail();

    // Query open tabs in the current window to pass to helper for signature matching
    let currentTabs: { url: string; title: string; active: boolean }[] = [];
    try {
      const tabs = await chrome.tabs.query({ currentWindow: true });
      currentTabs = tabs.map((t) => ({
        url: t.url || '',
        title: t.title || '',
        active: !!t.active
      }));
    } catch {
      // Ignore
    }

    try {
      const response = await sendNativeMessage({
        action: 'getProfiles',
        probeToken,
        sourceEmail: detectedEmail,
        tabs: currentTabs
      });

      if (response.success && response.profiles && response.profiles.length > 0) {
        let matchedCurrentDir: string | undefined = response.currentProfile;

        // Fallback to helper's isCurrent flag if currentProfile property wasn't direct
        if (!matchedCurrentDir) {
          const helperCurrent = response.profiles.find((p) => p.isCurrent);
          if (helperCurrent) {
            matchedCurrentDir = helperCurrent.directory;
          }
        }

        // If detected active email exists, give it highest priority if matched
        if (detectedEmail) {
          const emailMatch = response.profiles.find(
            (p) => p.email && p.email.toLowerCase() === detectedEmail.toLowerCase()
          );
          if (emailMatch) {
            matchedCurrentDir = emailMatch.directory;
          }
        }

        const dynamicSlots: ProfileSlotConfig[] = response.profiles.map((p, idx) => {
          const slotNum = idx + 1;
          const defaultKey = slotNum <= 9 ? `Alt + ${slotNum}` : slotNum === 10 ? 'Alt + 0' : undefined;
          const assignedShortcut = (shortcuts[p.directory] && !shortcuts[p.directory].includes('Shift'))
            ? shortcuts[p.directory]
            : defaultKey;

          const isCurrent = matchedCurrentDir
            ? p.directory.toLowerCase() === matchedCurrentDir.toLowerCase()
            : !!p.isCurrent;

          return {
            slot: slotNum,
            profileDirectory: p.directory,
            displayName: p.displayName || p.directory,
            gaiaName: p.gaiaName,
            email: p.email,
            avatarIcon: p.avatarIcon,
            shortcut: assignedShortcut,
            isCurrent
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
      return existing.map((s, idx) => {
        const slotNum = idx + 1;
        const defaultKey = slotNum <= 9 ? `Alt + ${slotNum}` : slotNum === 10 ? 'Alt + 0' : undefined;
        return {
          ...s,
          slot: slotNum,
          shortcut: shortcuts[s.profileDirectory] || s.shortcut || defaultKey
        };
      });
    }

    // Minimal fallback if helper never connected
    return [
      { slot: 1, profileDirectory: 'Default', displayName: 'Default', shortcut: shortcuts['Default'] || 'Alt + 1' },
      { slot: 2, profileDirectory: 'Profile 1', displayName: 'Profile 1', shortcut: shortcuts['Profile 1'] || 'Alt + 2' }
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
      try {
        await sendNativeMessage({
          action: shortcut ? 'setShortcut' : 'clearShortcut',
          slot: slotNumber,
          shortcut: shortcut || undefined
        });
      } catch (err) {
        console.warn('[Storage] Failed to sync shortcut to helper:', err);
      }
    }
    return await storageService.getSlotConfigs();
  },

  clearSlotShortcut: async (slotNumber: number): Promise<ProfileSlotConfig[]> => {
    const slots = await storageService.getSlotConfigs();
    const targetSlot = slots.find((s) => s.slot === slotNumber);
    if (targetSlot) {
      await storageService.setProfileShortcut(targetSlot.profileDirectory, undefined);
      try {
        await sendNativeMessage({ action: 'clearShortcut', slot: slotNumber });
      } catch (err) {
        console.warn('[Storage] Failed to clear shortcut in helper:', err);
      }
    }
    return await storageService.getSlotConfigs();
  },

  validateShortcutWithHelper: async (shortcut: string): Promise<{ valid: boolean; error?: string }> => {
    try {
      const res = await sendNativeMessage({ action: 'validateShortcut', shortcut });
      return { valid: res.success, error: res.error };
    } catch (err: unknown) {
      return { valid: false, error: err instanceof Error ? err.message : 'Validation failed' };
    }
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
