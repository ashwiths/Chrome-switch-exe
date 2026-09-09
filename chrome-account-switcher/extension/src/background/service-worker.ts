import { sendNativeMessage, storageService, NATIVE_HOST_NAME } from '../services/storage';
import { TabInfo } from '../types/account';

console.log(`[Background] Chrome Account Switcher service worker initialized. Extension ID: ${chrome.runtime.id}`);

// Maintain persistent native messaging port connection so helper stays online even when popup is closed
let persistentPort: chrome.runtime.Port | null = null;
let reconnectTimer: ReturnType<typeof setTimeout> | null = null;

function connectPersistentNativePort() {
  if (persistentPort) return;
  try {
    persistentPort = chrome.runtime.connectNative(NATIVE_HOST_NAME);
    console.log('[Background] Persistent native connection established.');

    persistentPort.onMessage.addListener((msg) => {
      console.log('[Background] Native host response:', msg);
    });

    persistentPort.onDisconnect.addListener(() => {
      const err = chrome.runtime.lastError?.message || 'Disconnected';
      console.warn('[Background] Persistent native port disconnected:', err);
      persistentPort = null;
      if (!reconnectTimer) {
        reconnectTimer = setTimeout(() => {
          reconnectTimer = null;
          connectPersistentNativePort();
        }, 2000);
      }
    });

    // Send initial ping to verify connection and start hook
    persistentPort.postMessage({ action: 'ping' });
  } catch (err) {
    console.error('[Background] Failed to connect native port:', err);
    if (!reconnectTimer) {
      reconnectTimer = setTimeout(() => {
        reconnectTimer = null;
        connectPersistentNativePort();
      }, 3000);
    }
  }
}

connectPersistentNativePort();

// Listen for keyboard shortcut commands from Chrome Commands API
chrome.commands.onCommand.addListener(async (command: string) => {
  console.log(`[Background] Chrome Command received: ${command}`);

  if (command.startsWith('switch-slot-')) {
    const slotNumber = parseInt(command.replace('switch-slot-', ''), 10);
    if (!isNaN(slotNumber) && slotNumber >= 1 && slotNumber <= 10) {
      await handleSwitchSlot(slotNumber);
    }
  }
});

// Handle slot or profile directory switching via native messaging
export async function handleSwitchSlot(slotNumber: number, profileDirectory?: string, copyTabs = false) {
  console.log(`[Background] Initiating fast switch to Slot ${slotNumber} (${profileDirectory || 'resolving...'}) [copyTabs=${copyTabs}]`);

  let validTabs: TabInfo[] = [];
  let skippedCount = 0;

  if (copyTabs) {
    try {
      let currentTabs = await chrome.tabs.query({ lastFocusedWindow: true });
      if (!currentTabs || currentTabs.length === 0) {
        currentTabs = await chrome.tabs.query({ active: true });
      }
      for (const tab of currentTabs) {
        if (tab.url && (tab.url.startsWith('http://') || tab.url.startsWith('https://'))) {
          validTabs.push({
            url: tab.url,
            title: tab.title || '',
            active: !!tab.active,
            index: tab.index
          });
        } else {
          skippedCount++;
        }
      }
    } catch (err) {
      console.warn('[Background] Failed to query current window tabs:', err);
    }
  }

  // If profileDirectory wasn't passed, resolve from dynamic slots
  let targetDir = profileDirectory;
  if (!targetDir) {
    try {
      const slots = await storageService.getSlotConfigs();
      const match = slots.find((s) => s.slot === slotNumber);
      if (match) {
        targetDir = match.profileDirectory;
      }
    } catch {
      // Ignore
    }
  }

  const response = await sendNativeMessage({
    action: 'switch-profile',
    slot: slotNumber,
    profileDirectory: targetDir,
    copyTabs: copyTabs,
    tabs: validTabs
  });

  if (response.success) {
    const info = copyTabs
      ? (response.tabsCopied !== undefined ? `${response.tabsCopied} tabs copied` : `${validTabs.length} tabs sent`)
      : 'Instant switch';
    const skipInfo = copyTabs && skippedCount > 0 ? ` (${skippedCount} skipped)` : '';
    const msg = `Switched to Slot ${slotNumber} (${response.displayName || response.profile || targetDir || 'Profile'}). ${info}${skipInfo}.`;
    console.log(`[Background] ${msg}`);
    await storageService.setLastStatus(msg);
  } else {
    const errMsg = `Slot ${slotNumber} Switch Failed: ${response.error || 'Unknown error'}`;
    console.error(`[Background] ${errMsg}`);
    await storageService.setLastStatus(errMsg);
  }

  return response;
}

// Listen for messages from extension popup and content script
chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message.action === 'trigger-custom-shortcut') {
    console.log(`[Background] Custom shortcut fired: ${message.combination} -> ${message.profileDirectory || 'Slot ' + message.slot}`);
    handleSwitchSlot(message.slot || 1, message.profileDirectory, message.copyTabs ?? false).then(sendResponse);
    return true;
  }
  if (message.action === 'switch-slot' && typeof message.slot === 'number') {
    handleSwitchSlot(message.slot, message.profileDirectory, message.copyTabs ?? false).then(sendResponse);
    return true; // async response
  }
  if (message.action === 'switch-profile' && message.profileDirectory) {
    handleSwitchSlot(message.slot || 1, message.profileDirectory, message.copyTabs ?? false).then(sendResponse);
    return true;
  }
});

// Auto-inject content script into open tabs on install/reload/startup
async function injectContentScriptIntoExistingTabs() {
  try {
    const tabs = await chrome.tabs.query({ url: ['http://*/*', 'https://*/*'] });
    for (const tab of tabs) {
      if (tab.id) {
        chrome.scripting.executeScript({
          target: { tabId: tab.id },
          files: ['content.js']
        }).catch(() => {});
      }
    }
  } catch (err) {
    // Ignore permissions or restricted tabs
  }
}

chrome.runtime.onInstalled.addListener(() => {
  injectContentScriptIntoExistingTabs();
  storageService.getSlotConfigs().catch(() => {});
});

chrome.runtime.onStartup.addListener(() => {
  injectContentScriptIntoExistingTabs();
  storageService.getSlotConfigs().catch(() => {});
});

// Warm up slot configs and inject content script on service worker startup
storageService.getSlotConfigs().catch(() => {});
injectContentScriptIntoExistingTabs().catch(() => {});


