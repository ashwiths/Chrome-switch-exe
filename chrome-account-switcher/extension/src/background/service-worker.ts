import { sendNativeMessage, storageService } from '../services/storage';

console.log('[Background] Chrome Account Switcher service worker initialized.');

// Listen for keyboard shortcut commands
chrome.commands.onCommand.addListener(async (command: string) => {
  console.log(`[Background] Received command: ${command}`);

  let slotNumber: number | null = null;
  if (command.startsWith('switch-slot-')) {
    const parsed = parseInt(command.replace('switch-slot-', ''), 10);
    if (!isNaN(parsed) && parsed >= 1 && parsed <= 5) {
      slotNumber = parsed;
    }
  }

  if (slotNumber !== null) {
    await handleSwitchSlot(slotNumber);
  }
});

// Handle slot switching via native messaging
export async function handleSwitchSlot(slotNumber: number) {
  console.log(`[Background] Initiating switch to Slot ${slotNumber}...`);

  const response = await sendNativeMessage({
    action: 'switch-profile',
    slot: slotNumber
  });

  if (response.success) {
    const msg = `Switched to Slot ${slotNumber} (${response.displayName || response.profile || 'Profile'}).`;
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
