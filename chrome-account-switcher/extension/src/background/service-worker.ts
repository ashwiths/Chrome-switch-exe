import { sendNativeMessage, storageService } from '../services/storage';
import { TabInfo } from '../types/account';

console.log(`[Background] Chrome Account Switcher service worker initialized. Extension ID: ${chrome.runtime.id}`);

// Listen for keyboard shortcut commands
chrome.commands.onCommand.addListener(async (command: string) => {
  console.log(`[Diagnostic] COMMAND RECEIVED: ${command}`);

  let slotNumber: number | null = null;
  if (command.startsWith('switch-slot-')) {
    const parsed = parseInt(command.replace('switch-slot-', ''), 10);
    if (!isNaN(parsed) && parsed >= 1 && parsed <= 5) {
      slotNumber = parsed;
    }
  }

  console.log(`[Diagnostic] SLOT RESOLVED: ${slotNumber}`);

  if (slotNumber !== null) {
    await handleSwitchSlot(slotNumber);
  }
});

// Handle slot switching via native messaging with tab copying
export async function handleSwitchSlot(slotNumber: number) {
  console.log(`[Background] Initiating switch to Slot ${slotNumber} with tab copying...`);

  let validTabs: TabInfo[] = [];
  let skippedCount = 0;

  try {
    const currentTabs = await chrome.tabs.query({ currentWindow: true, lastFocusedWindow: true });
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

  const response = await sendNativeMessage({
    action: 'switch-profile',
    slot: slotNumber,
    copyTabs: true,
    tabs: validTabs
  });

  if (response.success) {
    const copiedInfo = response.tabsCopied !== undefined ? `${response.tabsCopied} tabs copied` : `${validTabs.length} tabs sent`;
    const skipInfo = skippedCount > 0 ? ` (${skippedCount} skipped)` : '';
    const msg = `Switched to Slot ${slotNumber} (${response.displayName || response.profile || 'Profile'}). ${copiedInfo}${skipInfo}.`;
    console.log(`[Background] ${msg}`);
    await storageService.setLastStatus(msg);
  } else {
    const errMsg = `Slot ${slotNumber} Switch Failed: ${response.error || 'Unknown error'}`;
    console.error(`[Background] ${errMsg}`);
    await storageService.setLastStatus(errMsg);
  }

  return response;
}

// Listen for messages from extension popup
chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message.action === 'switch-slot' && typeof message.slot === 'number') {
    handleSwitchSlot(message.slot).then(sendResponse);
    return true; // async response
  }
});
