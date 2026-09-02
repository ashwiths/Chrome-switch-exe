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
      await onSave(slot.slot, recordedCombination);
      onClose();
    } catch (err: unknown) {
      setErrorMessage(err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal-card" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>Configure Shortcut — Slot {slot.slot}</h2>
          <p className="modal-subtitle">Press your desired key combination</p>
        </div>

        <div className="modal-body">
          <div className="current-row">
            <span className="info-label">Current:</span>
            <span className="current-badge">{slot.shortcut || 'Not Assigned'}</span>
          </div>

          <div className={`record-box ${isRecording ? 'recording' : ''}`}>
            {isRecording ? (
              <div className="recording-prompt">
                <span className="pulse-dot"></span>
                <span>{recordedCombination ? `${recordedCombination} + ...` : 'Press keys now (e.g. Alt + 2)'}</span>
              </div>
            ) : (
              <div className="recorded-display">
                <span className="combo-text">{recordedCombination || 'No key recorded'}</span>
              </div>
            )}
          </div>

          {errorMessage && (
            <div className="modal-error-banner">
              {errorMessage}
            </div>
          )}

          <div className="modal-action-row">
            <button
              className={`btn-record ${isRecording ? 'active' : ''}`}
              onClick={isRecording ? () => setIsRecording(false) : handleStartRecording}
            >
              {isRecording ? 'Stop Recording' : 'Record Shortcut'}
            </button>
            <button className="btn-clear" onClick={handleClear} disabled={saving}>
              Clear
            </button>
          </div>
        </div>

        <div className="modal-footer">
          <button className="btn-modal-cancel" onClick={onClose} disabled={saving}>
            Cancel
          </button>
          <button className="btn-modal-save" onClick={handleSave} disabled={saving || isRecording}>
            {saving ? 'Saving...' : 'Save'}
          </button>
        </div>
      </div>
    </div>
  );
};
