import { ProfileSlotConfig } from '../types/account';

/**
 * Normalizes a shortcut string for reliable comparison (e.g., "ctrl + alt + 1" -> "Ctrl+Alt+1").
 */
export function normalizeShortcut(shortcut?: string): string {
  if (!shortcut) return '';
  return shortcut
    .toLowerCase()
    .replace(/\s+/g, '')
    .split('+')
    .sort()
    .join('+');
}

/**
 * Known reserved browser or system shortcuts that cannot or should not be overridden.
 */
const RESERVED_SHORTCUTS = new Set([
  // Browser navigation and window management
  'ctrl+w',
  'ctrl+shift+w',
  'ctrl+t',
  'ctrl+shift+t',
  'ctrl+n',
  'ctrl+shift+n',
  'ctrl+q',
  'ctrl+shift+q',
  'ctrl+tab',
  'ctrl+shift+tab',
  'ctrl+r',
  'ctrl+shift+r',
  'f5',
  'ctrl+f5',
  'ctrl+h',
  'ctrl+j',
  'ctrl+d',
  'ctrl+p',
  'ctrl+s',
  'ctrl+o',
  'ctrl+f',
  'ctrl+g',
  'f11',
  'f12',
  'ctrl+shift+i',
  'ctrl+shift+j',
  'ctrl+shift+c',
  // Common editing
  'ctrl+c',
  'ctrl+v',
  'ctrl+x',
  'ctrl+a',
  'ctrl+z',
  'ctrl+y',
  // System critical
  'alt+f4',
  'alt+tab',
  'alt+shift+tab',
  'ctrl+alt+delete',
  'meta+l',
  'meta+d',
  'meta+tab'
]);

/**
 * Formats a native KeyboardEvent into a human-readable shortcut string.
 */
export function formatKeyboardEvent(e: KeyboardEvent): {
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

  // Format the primary key cleanly
  let primaryKey = e.key;

  // Handle Code / special keys
  if (e.code.startsWith('Key')) {
    primaryKey = e.code.replace('Key', '').toUpperCase();
  } else if (e.code.startsWith('Digit')) {
    primaryKey = e.code.replace('Digit', '');
  } else if (e.code.startsWith('Numpad')) {
    primaryKey = `Num ${e.code.replace('Numpad', '')}`;
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
      case 'Insert': primaryKey = 'Insert'; break;
      case 'Home': primaryKey = 'Home'; break;
      case 'End': primaryKey = 'End'; break;
      case 'PageUp': primaryKey = 'PageUp'; break;
      case 'PageDown': primaryKey = 'PageDown'; break;
      case 'Comma': primaryKey = ','; break;
      case 'Period': primaryKey = '.'; break;
      case 'Slash': primaryKey = '/'; break;
      case 'Semicolon': primaryKey = ';'; break;
      case 'Quote': primaryKey = "'"; break;
      case 'BracketLeft': primaryKey = '['; break;
      case 'BracketRight': primaryKey = ']'; break;
      case 'Backslash': primaryKey = '\\'; break;
      case 'Minus': primaryKey = '-'; break;
      case 'Equal': primaryKey = '='; break;
      case 'Backquote': primaryKey = '`'; break;
      default:
        // Handle F1-F12 or capital letter
        if (e.key.length === 1) {
          primaryKey = e.key.toUpperCase();
        } else {
          primaryKey = e.key;
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

/**
 * Validates a recorded shortcut combination.
 */
export function validateShortcut(
  combination: string,
  targetSlot: number,
  allSlots: ProfileSlotConfig[],
  hasModifier: boolean
): { valid: boolean; error?: string } {
  if (!combination.trim()) {
    return { valid: false, error: 'Shortcut combination cannot be empty.' };
  }

  // 1. Must have at least one modifier
  if (!hasModifier) {
    return {
      valid: false,
      error: 'Please include Ctrl, Alt, Shift, or Windows key.'
    };
  }

  const normalized = normalizeShortcut(combination);

  // 2. Check for system or browser reserved shortcuts
  if (RESERVED_SHORTCUTS.has(normalized)) {
    return {
      valid: false,
      error: 'This shortcut may be reserved by Chrome or Windows. Please choose another.'
    };
  }

  // 3. Check for conflict with other slots
  for (const s of allSlots) {
    if (s.slot !== targetSlot && s.shortcut) {
      if (normalizeShortcut(s.shortcut) === normalized) {
        return {
          valid: false,
          error: `Shortcut already assigned to Slot ${s.slot}.`
        };
      }
    }
  }

  return { valid: true };
}
