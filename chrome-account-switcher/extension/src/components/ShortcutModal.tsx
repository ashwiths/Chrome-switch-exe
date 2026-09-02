import React, { useState, useEffect } from 'react';
import { ProfileSlotConfig } from '../types/account';
import { formatKeyboardEvent, validateShortcut } from '../utils/shortcutValidator';

interface ShortcutModalProps {
  slot: ProfileSlotConfig;
  allSlots: ProfileSlotConfig[];
  onSave: (slotNumber: number, shortcut?: string) => Promise<void>;
  onClose: () => void;
}

export const ShortcutModal: React.FC<ShortcutModalProps> = ({
  slot,
  allSlots,
  onSave,
  onClose
}) => {
  const [isRecording, setIsRecording] = useState<boolean>(false);
  const [recordedCombination, setRecordedCombination] = useState<string>(slot.shortcut || '');
  const [hasModifier, setHasModifier] = useState<boolean>(!!slot.shortcut);
  const [errorMessage, setErrorMessage] = useState<string>('');
  const [saving, setSaving] = useState<boolean>(false);

  useEffect(() => {
    if (!isRecording) return;

    const handleKeyDown = (e: KeyboardEvent) => {
      e.preventDefault();
      e.stopPropagation();

      const formatted = formatKeyboardEvent(e);

      if (formatted.isModifierOnly) {
        setRecordedCombination(formatted.combination);
        setHasModifier(formatted.hasModifier);
      } else {
        setRecordedCombination(formatted.combination);
        setHasModifier(formatted.hasModifier);
        setIsRecording(false);
        setErrorMessage('');
      }
    };

    window.addEventListener('keydown', handleKeyDown, true);
    return () => {
      window.removeEventListener('keydown', handleKeyDown, true);
    };
  }, [isRecording]);

  const handleStartRecording = () => {
    setRecordedCombination('');
    setHasModifier(false);
    setErrorMessage('');
    setIsRecording(true);
  };

  const handleClear = async () => {
    setSaving(true);
    try {
      await onSave(slot.slot, undefined);
      onClose();
    } catch (err: unknown) {
      setErrorMessage(err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  };

  const handleSave = async () => {
    if (!recordedCombination.trim()) {
      handleClear();
      return;
    }

    const validation = validateShortcut(recordedCombination, slot.slot, allSlots, hasModifier);
    if (!validation.valid) {
      setErrorMessage(validation.error || 'Invalid shortcut.');
      return;
    }

    setSaving(true);
    try {
      // Validate directly with Windows Win32 RegisterHotKey via helper
      const { storageService } = await import('../services/storage');
      const win32Val = await storageService.validateShortcutWithHelper(recordedCombination);
      if (!win32Val.valid) {
        setErrorMessage(win32Val.error || 'Windows rejected this shortcut.');
        setSaving(false);
        return;
      }

      await onSave(slot.slot, recordedCombination);
      onClose();
    } catch (err: unknown) {
      setErrorMessage(err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  };

  return (
    <div
      className="fixed inset-0 bg-black/80 backdrop-blur-sm flex items-center justify-center z-50 p-4 animate-in fade-in duration-150"
      onClick={onClose}
    >
      <div
        className="bg-slate-900 border border-white/15 rounded-2xl w-full max-w-[340px] p-5 shadow-2xl shadow-black/80 flex flex-col gap-3.5"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex flex-col">
          <h2 className="text-sm font-bold text-white tracking-tight">
            Configure Shortcut — {slot.displayName || `Slot ${slot.slot}`}
          </h2>
          <p className="text-[11px] text-slate-400 mt-0.5">
            Press your desired key combination (e.g. Alt + 2)
          </p>
        </div>

        <div className="flex flex-col gap-2.5">
          <div className="flex items-center justify-between text-xs">
            <span className="text-slate-400">Current Shortcut:</span>
            <span className="bg-slate-950 px-2 py-0.5 rounded font-mono text-[11px] font-semibold text-sky-400 border border-white/10">
              {slot.shortcut || 'Not Assigned'}
            </span>
          </div>

          <div
            className={`min-h-[54px] rounded-xl border-2 flex items-center justify-center p-3 text-center transition-all ${
              isRecording
                ? 'border-sky-400 bg-sky-500/10 shadow-glow'
                : 'border-dashed border-white/15 bg-slate-950/70'
            }`}
          >
            {isRecording ? (
              <div className="flex items-center gap-2 text-sky-400 text-xs font-semibold">
                <span className="w-2 h-2 rounded-full bg-rose-500 animate-pulse-custom"></span>
                <span>{recordedCombination ? `${recordedCombination} + ...` : 'Press keys now...'}</span>
              </div>
            ) : (
              <span className="text-sm font-bold font-mono text-white tracking-wide">
                {recordedCombination || <span className="text-slate-500 text-xs font-normal">Click Record Shortcut to begin</span>}
              </span>
            )}
          </div>

          {errorMessage && (
            <div className="bg-rose-500/15 border-l-4 border-rose-500 text-rose-300 px-2.5 py-1.5 rounded text-[11px] leading-tight">
              {errorMessage}
            </div>
          )}

          <div className="flex gap-2 pt-1">
            <button
              className={`flex-1 py-2 px-3 rounded-lg text-xs font-semibold transition-all ${
                isRecording
                  ? 'bg-rose-600 hover:bg-rose-700 text-white shadow-md shadow-rose-600/30'
                  : 'bg-sky-600 hover:bg-sky-500 text-white shadow-md shadow-sky-600/30'
              }`}
              onClick={isRecording ? () => setIsRecording(false) : handleStartRecording}
            >
              {isRecording ? 'Stop Recording' : 'Record Shortcut'}
            </button>
            <button
              className="py-2 px-3 rounded-lg text-xs font-medium bg-white/5 hover:bg-white/10 text-slate-300 border border-white/10 transition-all"
              onClick={handleClear}
              disabled={saving}
            >
              Clear
            </button>
          </div>
        </div>

        <div className="border-t border-white/10 pt-3 flex justify-end gap-2">
          <button
            className="py-1.5 px-3 rounded-lg text-xs text-slate-400 hover:text-white transition-all"
            onClick={onClose}
            disabled={saving}
          >
            Cancel
          </button>
          <button
            className="py-1.5 px-4 rounded-lg text-xs font-semibold bg-emerald-600 hover:bg-emerald-500 text-white shadow-md shadow-emerald-600/30 disabled:opacity-50 transition-all"
            onClick={handleSave}
            disabled={saving || isRecording}
          >
            {saving ? 'Saving...' : 'Save'}
          </button>
        </div>
      </div>
    </div>
  );
};
