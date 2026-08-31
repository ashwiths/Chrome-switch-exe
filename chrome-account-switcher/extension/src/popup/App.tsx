import React, { useEffect, useState } from 'react';
import './popup.css';
import { storageService, sendNativeMessage } from '../services/storage';
import { ProfileSlotConfig } from '../types/account';

export const App: React.FC = () => {
  const [slots, setSlots] = useState<ProfileSlotConfig[]>([]);
  const [lastStatus, setLastStatus] = useState<string>('');
  const [loadingSlot, setLoadingSlot] = useState<number | null>(null);
  const [hostStatus, setHostStatus] = useState<'checking' | 'connected' | 'error'>('checking');
  const [hostError, setHostError] = useState<string>('');

  const loadData = async () => {
    const loadedSlots = await storageService.getSlotConfigs();
    setSlots(loadedSlots);

    const savedStatus = await storageService.getLastStatus();
    if (savedStatus) {
      setLastStatus(savedStatus.message);
    }

    // Ping native helper
    const pingRes = await sendNativeMessage({ action: 'ping' });
    if (pingRes.success) {
      setHostStatus('connected');
      setHostError('');
    } else {
      setHostStatus('error');
      setHostError(pingRes.error || 'Native helper not reachable');
    }
  };

  useEffect(() => {
    loadData();
  }, []);

  const handleTriggerSlot = async (slotNumber: number) => {
    setLoadingSlot(slotNumber);
    setLastStatus(`Switching to Slot ${slotNumber}...`);

    try {
      const response = await sendNativeMessage({
        action: 'switch-profile',
        slot: slotNumber
      });

      if (response.success) {
        const msg = `Success: Focused ${response.displayName || response.profile || 'Profile'} (HWND: 0x${(response.windowHandle || 0).toString(16).toUpperCase()})`;
        setLastStatus(msg);
        await storageService.setLastStatus(msg);
      } else {
        const msg = `Error: ${response.error || 'Failed to switch'}`;
        setLastStatus(msg);
        await storageService.setLastStatus(msg);
      }
    } catch (err: unknown) {
      const msg = `Error: ${err instanceof Error ? err.message : String(err)}`;
      setLastStatus(msg);
    } finally {
      setLoadingSlot(null);
    }
  };

  return (
    <div className="popup-container">
      <header className="header">
        <div className="title-row">
          <h1>Chrome Account Switcher</h1>
          <span className={`status-pill ${hostStatus}`}>
            {hostStatus === 'connected' ? 'Helper Connected' : hostStatus === 'checking' ? 'Checking...' : 'Helper Offline'}
          </span>
        </div>
        <p className="subtitle">Instant State-Preserving Profile Switching</p>
      </header>

      {hostStatus === 'error' && (
        <div className="alert-warning">
          <strong>Native Helper Notice:</strong> {hostError}
          <div className="alert-sub">Make sure the native host is registered in the Windows registry.</div>
        </div>
      )}

      {lastStatus && (
        <div className="last-status-banner">
          <span className="status-label">Last Action:</span> {lastStatus}
        </div>
      )}

      <section className="slots-section">
        <h3>Configured Profile Slots</h3>
        <div className="slots-list">
          {slots.map((item) => (
            <div key={item.slot} className="slot-card">
              <div className="slot-info">
                <div className="slot-badge-row">
                  <span className="slot-badge">Slot {item.slot}</span>
                  <span className="shortcut-badge">Ctrl + {item.slot}</span>
                </div>
                <div className="slot-details">
                  <span className="profile-dir">{item.profileDirectory}</span>
                  {item.displayName && <span className="profile-name">({item.displayName})</span>}
                </div>
              </div>
              <button
                className="btn-switch"
                disabled={loadingSlot === item.slot || hostStatus !== 'connected'}
                onClick={() => handleTriggerSlot(item.slot)}
              >
                {loadingSlot === item.slot ? 'Focusing...' : 'Switch'}
              </button>
            </div>
          ))}
        </div>
      </section>

      <footer className="footer-note">
        <span>Use <strong>Ctrl + 1..5</strong> to switch instantly without reloading tabs.</span>
      </footer>
    </div>
  );
};

export default App;
