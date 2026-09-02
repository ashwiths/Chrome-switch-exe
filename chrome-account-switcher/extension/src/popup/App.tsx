import React, { useEffect, useState } from 'react';
import './popup.css';
import { storageService, sendNativeMessage } from '../services/storage';
import { ProfileSlotConfig } from '../types/account';
import { ShortcutModal } from '../components/ShortcutModal';

const getAvatarStyle = (dir: string) => {
  let hash = 0;
  for (let i = 0; i < dir.length; i++) {
    hash = dir.charCodeAt(i) + ((hash << 5) - hash);
  }
  const hue = Math.abs(hash % 360);
  return {
    background: `hsl(${hue}, 45%, 22%)`,
    color: `hsl(${hue}, 85%, 75%)`,
    border: `1px solid hsl(${hue}, 50%, 35%)`
  };
};

const getInitial = (name: string) => {
  if (!name) return '?';
  const clean = name.replace(/[^a-zA-Z0-9]/g, '');
  return (clean[0] || name[0] || '?').toUpperCase();
};

export const App: React.FC = () => {
  const [slots, setSlots] = useState<ProfileSlotConfig[]>([]);
  const [lastStatus, setLastStatus] = useState<string>('');
  const [loadingSlot, setLoadingSlot] = useState<number | null>(null);
  const [hostStatus, setHostStatus] = useState<'checking' | 'connected' | 'error'>('checking');
  const [hostError, setHostError] = useState<string>('');
  const [editingSlot, setEditingSlot] = useState<ProfileSlotConfig | null>(null);
  const [isRefreshing, setIsRefreshing] = useState<boolean>(false);

  const loadData = async () => {
    setIsRefreshing(true);
    try {
      // 1. Ping native helper and query dynamic profiles
      const pingRes = await sendNativeMessage({ action: 'ping' });
      if (pingRes.success) {
        setHostStatus('connected');
        setHostError('');
      } else {
        setHostStatus('error');
        setHostError(pingRes.error || 'Native helper not reachable');
      }

      // 2. Discover dynamic profiles and attach directory-bound shortcuts
      const loadedSlots = await storageService.getSlotConfigs();
      setSlots(loadedSlots);

      // 3. Sync slots with native helper
      if (pingRes.success) {
        sendNativeMessage({ action: 'sync-slots', slots: loadedSlots });
      }

      const savedStatus = await storageService.getLastStatus();
      if (savedStatus) {
        setLastStatus(savedStatus.message);
      }
    } finally {
      setIsRefreshing(false);
    }
  };

  useEffect(() => {
    loadData();
  }, []);

  const handleTriggerSlot = async (slotNumber: number, profileDirectory?: string) => {
    setLoadingSlot(slotNumber);
    const targetDir = profileDirectory || slots.find((s) => s.slot === slotNumber)?.profileDirectory;
    setLastStatus(`Switching to Slot ${slotNumber} (${targetDir || 'Profile'})...`);

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
        profileDirectory: targetDir,
        copyTabs: true,
        tabs: validTabs
      });

      if (response.success) {
        const copiedInfo = response.tabsCopied !== undefined ? `${response.tabsCopied} tabs copied` : `${validTabs.length} tabs sent`;
        const skipInfo = skippedCount > 0 ? ` (${skippedCount} skipped)` : '';
        const targetName = response.displayName || response.profile || targetDir || 'Profile';
        const msg = `Success: Focused ${targetName}. ${copiedInfo}${skipInfo}.`;
        setLastStatus(msg);
        await storageService.setLastStatus(msg);
        // Refresh active state
        loadData();
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

  const handleSaveShortcut = async (slotNumber: number, shortcut?: string) => {
    const updated = await storageService.updateSlotShortcut(slotNumber, shortcut);
    setSlots(updated);

    // Sync with native helper
    const targetSlot = updated.find((s) => s.slot === slotNumber);
    await sendNativeMessage({
      action: 'set-shortcut',
      slot: slotNumber,
      profileDirectory: targetSlot?.profileDirectory,
      shortcut
    });

    const statusMsg = shortcut
      ? `Shortcut for '${targetSlot?.displayName || targetSlot?.profileDirectory}' set to '${shortcut}'.`
      : `Shortcut for '${targetSlot?.displayName || targetSlot?.profileDirectory}' cleared.`;
    setLastStatus(statusMsg);
    await storageService.setLastStatus(statusMsg);
  };

  return (
    <div className="popup-container">
      <header className="header">
        <div className="title-row">
          <h1>Chrome Account Switcher</h1>
          <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
            <button
              className="btn-refresh"
              onClick={loadData}
              disabled={isRefreshing}
              title="Refresh discovered Chrome profiles"
            >
              {isRefreshing ? 'Scanning...' : 'Refresh'}
            </button>
            <span className={`status-pill ${hostStatus}`}>
              {hostStatus === 'connected' ? 'Helper Connected' : hostStatus === 'checking' ? 'Checking...' : 'Helper Offline'}
            </span>
          </div>
        </div>
        <p className="subtitle">
          {slots.length} Chrome {slots.length === 1 ? 'Profile' : 'Profiles'} Discovered
        </p>
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
        <h3>Discovered Chrome Profile Slots</h3>
        <div className="slots-list">
          {slots.map((item) => (
            <div key={item.profileDirectory} className={`slot-card ${item.isCurrent ? 'is-current' : ''}`}>
              <div className="slot-main">
                <div
                  className={`profile-avatar ${item.isCurrent ? 'current-avatar' : ''}`}
                  style={getAvatarStyle(item.profileDirectory)}
                  title={item.profileDirectory}
                >
                  {getInitial(item.displayName || item.profileDirectory)}
                </div>

                <div className="slot-info">
                  <div className="slot-badge-row">
                    <span className="slot-badge">Slot {item.slot}</span>
                    {item.isCurrent && <span className="current-badge-pill">● CURRENT</span>}
                    <span className={`shortcut-badge ${item.shortcut ? '' : 'click-badge'}`}>
                      {item.shortcut || 'Not Assigned'}
                    </span>
                  </div>
                  <div className="slot-details">
                    <span className="profile-name">
                      {item.displayName || item.profileDirectory}
                      {item.gaiaName && item.gaiaName !== item.displayName && (
                        <span style={{ color: '#94a3b8', fontSize: '11px', fontWeight: 'normal', marginLeft: '4px' }}>
                          ({item.gaiaName})
                        </span>
                      )}
                    </span>
                    {item.email && <span className="profile-email">{item.email}</span>}
                    <span className="profile-dir">{item.profileDirectory}</span>
                  </div>
                </div>
              </div>

              <div className="slot-actions">
                <button
                  className="btn-custom-key"
                  onClick={() => setEditingSlot(item)}
                  title={`Configure keyboard shortcut for ${item.displayName || item.profileDirectory}`}
                >
                  Custom Key
                </button>
                <button
                  className="btn-switch"
                  disabled={loadingSlot === item.slot || hostStatus !== 'connected'}
                  onClick={() => handleTriggerSlot(item.slot, item.profileDirectory)}
                >
                  {loadingSlot === item.slot ? 'Focusing...' : 'Switch'}
                </button>
              </div>
            </div>
          ))}
        </div>
      </section>

      <footer className="footer-note">
        <span>Press assigned shortcut or click <strong>Switch</strong> to change profile.</span>
      </footer>

      {editingSlot && (
        <ShortcutModal
          slot={editingSlot}
          allSlots={slots}
          onSave={handleSaveShortcut}
          onClose={() => setEditingSlot(null)}
        />
      )}
    </div>
  );
};

export default App;


