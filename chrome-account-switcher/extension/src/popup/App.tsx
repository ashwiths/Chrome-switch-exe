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
    setLastStatus(`Switching to Slot ${slotNumber} (with tab copying)...`);

    try {
      const validTabs = [];
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
        console.warn('Failed to query current window tabs:', err);
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
        const msg = `Success: Focused ${response.displayName || response.profile || 'Profile'}. ${copiedInfo}${skipInfo}.`;
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

      <div style={{ marginBottom: '10px', padding: '6px 8px', background: 'rgba(255,255,255,0.04)', borderRadius: '4px', fontSize: '10px', color: '#94a3b8', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <span>ID: <code style={{ color: '#38bdf8' }}>{chrome?.runtime?.id || 'Unknown'}</code></span>
        <button
          style={{ padding: '3px 8px', background: '#2563eb', color: '#fff', border: 'none', borderRadius: '3px', cursor: 'pointer', fontSize: '10px', fontWeight: 600 }}
          onClick={() => handleTriggerSlot(2)}
          disabled={loadingSlot === 2}
        >
          {loadingSlot === 2 ? 'Testing...' : 'TEST SLOT 2'}
        </button>
      </div>

      <section className="slots-section">
        <h3>Configured Profile Slots</h3>
        <div className="slots-list">
          {slots.map((item) => (
            <div key={item.slot} className="slot-card">
              <div className="slot-info">
                <div className="slot-badge-row">
                  <span className="slot-badge">Slot {item.slot}</span>
                  {item.slot <= 4 ? (
                    <span className="shortcut-badge">Alt + Shift + {item.slot}</span>
                  ) : (
                    <span className="shortcut-badge click-badge">Click / Popup</span>
                  )}
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
        <span>Use <strong>Alt + Shift + 1..4</strong> or click any slot to switch instantly.</span>
      </footer>
    </div>
  );
};

export default App;
