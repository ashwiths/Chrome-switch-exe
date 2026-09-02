// Self-contained in-page content script (Classic script compatible with Chrome MV3)

(() => {
  if ((window as any).__CHROME_ACCOUNT_SWITCHER_LOADED__) {
    return;
  }
  (window as any).__CHROME_ACCOUNT_SWITCHER_LOADED__ = true;

  function normalizeShortcut(shortcut?: string): string {
    if (!shortcut) return '';
    return shortcut
      .toLowerCase()
      .replace(/\s+/g, '')
      .split('+')
      .sort()
      .join('+');
  }

  function formatKeyboardEvent(e: KeyboardEvent): {
    combination: string;
    hasModifier: boolean;
    isModifierOnly: boolean;
    primaryKey: string;
  } {
    const isModifierKey = ['Control', 'Alt', 'Shift', 'Meta'].includes(e.key);
    const parts: string[] = [];

    if (e.ctrlKey) parts.push('Ctrl');
    if (e.altKey) parts.push('Alt');
    if (e.shiftKey) parts.push('Shift');
    if (e.metaKey) parts.push('Win');

    const hasModifier = parts.length > 0;

    if (isModifierKey) {
      return {
        combination: parts.join(' + '),
        hasModifier,
        isModifierOnly: true,
        primaryKey: ''
      };
    }

    let primaryKey = e.key;
    if (e.code.startsWith('Key')) {
      primaryKey = e.code.replace('Key', '').toUpperCase();
    } else if (e.code.startsWith('Digit')) {
      primaryKey = e.code.replace('Digit', '');
    } else if (e.code.startsWith('Numpad')) {
      primaryKey = e.code.replace('Numpad', '');
    } else if (e.code.startsWith('Arrow')) {
      primaryKey = e.code.replace('Arrow', '');
    } else {
      switch (e.code) {
        case 'Space': primaryKey = 'Space'; break;
        case 'Enter': primaryKey = 'Enter'; break;
        case 'Escape': primaryKey = 'Esc'; break;
        case 'Tab': primaryKey = 'Tab'; break;
        case 'Backspace': primaryKey = 'Backspace'; break;
        case 'Delete': primaryKey = 'Delete'; break;
        default:
          if (e.key && e.key.length === 1) {
            primaryKey = e.key.toUpperCase();
          } else {
            primaryKey = e.key || '';
          }
          break;
      }
    }

    parts.push(primaryKey);

    return {
      combination: parts.join(' + '),
      hasModifier,
      isModifierOnly: false,
      primaryKey
    };
  }

  let activeShortcuts: Record<string, { directory: string; slot: number }> = {};

  async function loadShortcuts() {
    try {
      const data = await chrome.storage.local.get(['profileSlots', 'profileShortcuts']);
      const map: Record<string, { directory: string; slot: number }> = {};

      // 1. From profileSlots array
      if (data.profileSlots && Array.isArray(data.profileSlots)) {
        for (const s of data.profileSlots) {
          if (s.shortcut && s.profileDirectory) {
            map[normalizeShortcut(s.shortcut)] = { directory: s.profileDirectory, slot: s.slot };
          }
        }
      }

      // 2. From profileShortcuts map (directory -> shortcut)
      if (data.profileShortcuts && typeof data.profileShortcuts === 'object') {
        for (const [dir, sc] of Object.entries(data.profileShortcuts as Record<string, string>)) {
          if (sc) {
            const norm = normalizeShortcut(sc);
            const existing = map[norm];
            map[norm] = { directory: dir, slot: existing?.slot || 1 };
          }
        }
      }

      // 3. Built-in defaults: Alt + 1..9 and Alt + 0
      for (let i = 1; i <= 10; i++) {
        const key = i <= 9 ? `Alt + ${i}` : 'Alt + 0';
        const norm = normalizeShortcut(key);
        if (!map[norm]) {
          // If not configured, map to corresponding slot directory from profileSlots if available
          const matchingSlot = data.profileSlots?.[i - 1];
          if (matchingSlot) {
            map[norm] = { directory: matchingSlot.profileDirectory, slot: matchingSlot.slot };
          }
        }
      }

      activeShortcuts = map;
      console.log('[Chrome Switcher] Active shortcuts ready:', activeShortcuts);
    } catch (err) {
      console.debug('[Chrome Switcher] Failed to load shortcuts:', err);
    }
  }

  loadShortcuts();

  // Storage change listener
  chrome.storage.onChanged.addListener((changes, areaName) => {
    if (areaName === 'local' && (changes.profileSlots || changes.profileShortcuts)) {
      loadShortcuts();
    }
  });

  // Window keydown listener (capture phase)
  window.addEventListener(
    'keydown',
    (e: KeyboardEvent) => {
      // Must have Alt or Ctrl modifier
      if (!e.altKey && !e.ctrlKey) {
        return;
      }

      const isInput =
        e.target instanceof HTMLInputElement ||
        e.target instanceof HTMLTextAreaElement ||
        (e.target instanceof HTMLElement && e.target.isContentEditable);

      if (isInput && !e.altKey) {
        return;
      }
      const formatted = formatKeyboardEvent(e);
      if (formatted.isModifierOnly) {
        return;
      }

      const norm = normalizeShortcut(formatted.combination);
      const target = activeShortcuts[norm];

      if (target) {
        console.log(`[Chrome Switcher] TRIGGERED: ${formatted.combination} -> Slot ${target.slot} (${target.directory})`);
        e.preventDefault();
        e.stopPropagation();

        chrome.runtime.sendMessage({
          action: 'trigger-custom-shortcut',
          combination: formatted.combination,
          profileDirectory: target.directory,
          slot: target.slot
        });
      }
    },
    true
  );
})();
