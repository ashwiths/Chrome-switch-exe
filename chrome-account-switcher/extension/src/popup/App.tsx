import React, { useEffect, useState, useMemo } from 'react';
import './popup.css';
import { storageService, sendNativeMessage } from '../services/storage';
import { ProfileSlotConfig } from '../types/account';
import { ShortcutModal } from '../components/ShortcutModal';

const getAvatarGradient = (dir: string) => {
  let hash = 0;
  for (let i = 0; i < dir.length; i++) {
    hash = dir.charCodeAt(i) + ((hash << 5) - hash);
  }
  const hue = Math.abs(hash % 360);
  return {
    background: `linear-gradient(135deg, hsl(${hue}, 65%, 28%) 0%, hsl(${(hue + 40) % 360}, 75%, 20%) 100%)`,
    color: `hsl(${hue}, 95%, 85%)`,
    borderColor: `hsl(${hue}, 60%, 38%)`
  };
};

const getInitial = (name: string, gaiaName?: string) => {
  const target = gaiaName || name || '?';
  const clean = target.replace(/[^a-zA-Z0-9]/g, '');
  return (clean[0] || target[0] || '?').toUpperCase();
};

export const App: React.FC = () => {
  const [slots, setSlots] = useState<ProfileSlotConfig[]>([]);
  const [lastStatus, setLastStatus] = useState<string>('');
  const [loadingSlot, setLoadingSlot] = useState<number | null>(null);
  const [hostStatus, setHostStatus] = useState<'checking' | 'connected' | 'error'>('checking');
  const [hostError, setHostError] = useState<string>('');
  const [editingSlot, setEditingSlot] = useState<ProfileSlotConfig | null>(null);
  const [isRefreshing, setIsRefreshing] = useState<boolean>(false);
  const [searchQuery, setSearchQuery] = useState<string>('');

  const loadData = async () => {
    setIsRefreshing(true);
    try {
      // 1. Ping native helper
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

  const filteredSlots = useMemo(() => {
    if (!searchQuery.trim()) return slots;
    const q = searchQuery.toLowerCase().trim();
    return slots.filter((s) => {
      return (
        s.displayName?.toLowerCase().includes(q) ||
        s.gaiaName?.toLowerCase().includes(q) ||
        s.email?.toLowerCase().includes(q) ||
        s.profileDirectory?.toLowerCase().includes(q) ||
        s.shortcut?.toLowerCase().includes(q) ||
        `slot ${s.slot}`.includes(q)
      );
    });
  }, [slots, searchQuery]);

  const handleTriggerSlot = async (slotNumber: number, profileDirectory?: string) => {
    setLoadingSlot(slotNumber);
    const targetDir = profileDirectory || slots.find((s) => s.slot === slotNumber)?.profileDirectory;
    setLastStatus(`Switching to ${targetDir || 'Profile'}...`);

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
        const msg = `Switched to ${targetName}. ${copiedInfo}${skipInfo}.`;
        setLastStatus(msg);
        await storageService.setLastStatus(msg);
        loadData();
      } else {
        const msg = `Switch Failed: ${response.error || 'Unknown error'}`;
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
    <div className="p-3.5 flex flex-col gap-3 min-h-full bg-[radial-gradient(ellipse_at_top_right,_var(--tw-gradient-stops))] from-blue-900/15 via-transparent to-emerald-950/10">
      {/* Header */}
      <header className="flex flex-col gap-2.5 pb-2.5 border-b border-white/10">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <div className="w-6 h-6 rounded-md bg-gradient-to-br from-blue-600 to-sky-400 flex items-center justify-center text-white text-xs font-bold shadow-md shadow-sky-500/20">
              ⇄
            </div>
            <div className="flex flex-col">
              <h1 className="text-sm font-bold text-white tracking-tight leading-none">
                Chrome Switcher
              </h1>
              <span className="text-[10px] text-slate-400 font-medium mt-0.5">
                {slots.length} {slots.length === 1 ? 'profile' : 'profiles'} discovered
              </span>
            </div>
          </div>

          <div className="flex items-center gap-1.5">
            <button
              className={`w-6 h-6 rounded flex items-center justify-center bg-white/5 hover:bg-white/10 border border-white/10 text-slate-300 hover:text-white transition-all text-xs ${
                isRefreshing ? 'animate-spin-custom' : ''
              }`}
              onClick={loadData}
              disabled={isRefreshing}
              title="Refresh detected Chrome profiles"
            >
              ↻
            </button>
            <span
              className={`text-[10px] font-semibold px-2 py-0.5 rounded-full flex items-center gap-1.5 border ${
                hostStatus === 'connected'
                  ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/30'
                  : hostStatus === 'checking'
                  ? 'bg-slate-500/10 text-slate-400 border-slate-500/20'
                  : 'bg-rose-500/10 text-rose-400 border-rose-500/30'
              }`}
              title={hostError || 'Native helper connection'}
            >
              <span
                className={`w-1.5 h-1.5 rounded-full ${
                  hostStatus === 'connected'
                    ? 'bg-emerald-400 shadow-[0_0_6px_rgba(52,211,153,0.8)]'
                    : hostStatus === 'checking'
                    ? 'bg-slate-400'
                    : 'bg-rose-400'
                }`}
              ></span>
              {hostStatus === 'connected' ? 'Connected' : hostStatus === 'checking' ? 'Checking' : 'Offline'}
            </span>
          </div>
        </div>

        {/* Quick Search */}
        {slots.length > 3 && (
          <div className="relative w-full">
            <span className="absolute left-2.5 top-1/2 -translate-y-1/2 text-xs text-slate-400 pointer-events-none">
              🔍
            </span>
            <input
              type="text"
              className="w-full bg-slate-900/70 border border-white/10 rounded-lg py-1.5 pl-8 pr-7 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-sky-400 focus:ring-1 focus:ring-sky-400/30 transition-all"
              placeholder="Search by name, email, or slot..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
            {searchQuery && (
              <button
                className="absolute right-2 top-1/2 -translate-y-1/2 text-xs text-slate-400 hover:text-white p-1"
                onClick={() => setSearchQuery('')}
              >
                ✕
              </button>
            )}
          </div>
        )}
      </header>

      {/* Last Status Banner */}
      {lastStatus && (
        <div className="bg-slate-900/80 backdrop-blur-md border border-sky-500/30 border-l-2 border-l-sky-400 rounded-md px-2.5 py-1.5 text-[11px] text-slate-200 flex items-center gap-2">
          <span className="text-sky-400 text-xs">ℹ</span>
          <span className="truncate">{lastStatus}</span>
        </div>
      )}

      {/* Profiles List */}
      <section className="flex flex-col gap-2">
        <div className="flex items-center justify-between px-0.5">
          <span className="text-[10px] font-bold uppercase tracking-wider text-slate-400">
            Profiles
          </span>
          <span className="text-[10px] text-slate-400 bg-white/5 px-2 py-0.5 rounded-full font-mono">
            {filteredSlots.length} of {slots.length}
          </span>
        </div>

        <div className="flex flex-col gap-2">
          {filteredSlots.map((item) => {
            const avatarStyle = getAvatarGradient(item.profileDirectory);
            return (
              <div
                key={item.profileDirectory}
                className={`group relative flex items-center justify-between p-2.5 rounded-xl border backdrop-blur-md transition-all duration-200 ${
                  item.isCurrent
                    ? 'border-emerald-500/50 bg-gradient-to-r from-emerald-500/10 via-slate-900/80 to-slate-900/70 shadow-md shadow-emerald-950/30 before:absolute before:left-0 before:top-0 before:bottom-0 before:w-1 before:bg-emerald-400 before:rounded-l-xl'
                    : 'border-white/[0.08] bg-slate-900/60 hover:bg-slate-850 hover:border-sky-500/40 hover:shadow-lg hover:shadow-black/40'
                }`}
              >
                <div className="flex items-center gap-2.5 min-w-0 flex-1">
                  {/* Avatar */}
                  <div
                    className={`w-8 h-8 rounded-full flex items-center justify-center font-bold text-xs shrink-0 shadow-md transition-transform group-hover:scale-105 border ${
                      item.isCurrent ? 'ring-2 ring-emerald-400/80 ring-offset-1 ring-offset-slate-900' : ''
                    }`}
                    style={avatarStyle}
                    title={`Profile: ${item.profileDirectory}`}
                  >
                    {getInitial(item.displayName || item.profileDirectory, item.gaiaName)}
                  </div>

                  {/* Profile Info */}
                  <div className="flex flex-col min-w-0 flex-1">
                    <div className="flex items-center gap-1.5 flex-wrap">
                      <span className="text-[9px] font-bold text-sky-400 bg-sky-500/10 px-1.5 py-0.5 rounded font-mono">
                        Slot {item.slot}
                      </span>
                      {item.isCurrent && (
                        <span className="text-[8px] font-bold text-emerald-400 bg-emerald-500/15 border border-emerald-500/35 px-1.5 py-0.5 rounded-full flex items-center gap-1 tracking-wide">
                          <span className="w-1 h-1 rounded-full bg-emerald-400 animate-pulse-custom"></span> CURRENT
                        </span>
                      )}
                      {item.shortcut ? (
                        <span className="text-[9px] font-mono font-semibold px-1.5 py-0.5 rounded bg-slate-950 text-slate-200 border border-white/10">
                          {item.shortcut}
                        </span>
                      ) : (
                        <span className="text-[9px] text-slate-400 px-1 py-0.5 rounded bg-white/[0.02]">
                          No Key
                        </span>
                      )}
                    </div>

                    <div
                      className="text-xs font-semibold text-white truncate mt-0.5 tracking-tight"
                      title={item.displayName || item.profileDirectory}
                    >
                      {item.displayName || item.profileDirectory}
                      {item.gaiaName && item.gaiaName !== item.displayName && (
                        <span className="text-[11px] font-normal text-slate-400 ml-1">
                          ({item.gaiaName})
                        </span>
                      )}
                    </div>

                    <div className="flex items-center gap-1.5 truncate mt-0.5">
                      {item.email && (
                        <span className="text-[10px] text-slate-400 truncate" title={item.email}>
                          {item.email}
                        </span>
                      )}
                      <span className="text-[9px] font-mono text-slate-400 bg-white/5 px-1 rounded shrink-0">
                        {item.profileDirectory}
                      </span>
                    </div>
                  </div>
                </div>

                {/* Actions */}
                <div className="flex items-center gap-1.5 ml-2 shrink-0">
                  <button
                    className="text-[10px] font-medium px-2 py-1 rounded-md bg-white/5 text-slate-300 border border-white/10 hover:bg-white/10 hover:text-white hover:border-white/20 active:scale-95 transition-all"
                    onClick={() => setEditingSlot(item)}
                    title={`Configure shortcut for ${item.displayName || item.profileDirectory}`}
                  >
                    ⌨ Key
                  </button>
                  <button
                    className={`text-[11px] font-semibold px-3 py-1 rounded-md transition-all active:scale-95 disabled:opacity-50 disabled:cursor-not-allowed ${
                      item.isCurrent
                        ? 'bg-emerald-500/20 text-emerald-400 border border-emerald-500/40 hover:bg-emerald-500/30'
                        : 'bg-gradient-to-r from-blue-600 to-sky-500 text-white shadow-md shadow-blue-600/30 hover:brightness-110 hover:shadow-sky-500/40'
                    }`}
                    disabled={loadingSlot === item.slot || hostStatus !== 'connected'}
                    onClick={() => handleTriggerSlot(item.slot, item.profileDirectory)}
                    title={item.isCurrent ? 'Currently active profile' : 'Switch to this profile'}
                  >
                    {loadingSlot === item.slot ? 'Focusing...' : item.isCurrent ? 'Active' : '⇄ Switch'}
                  </button>
                </div>
              </div>
            );
          })}

          {filteredSlots.length === 0 && (
            <div className="text-center py-6 text-slate-400 text-xs bg-white/[0.02] border border-dashed border-white/10 rounded-xl">
              No profiles matching "<strong>{searchQuery}</strong>"
            </div>
          )}
        </div>
      </section>

      {/* Direct link to Chrome's native shortcuts manager */}
      <div className="bg-sky-500/10 border border-sky-500/25 rounded-lg p-2 flex items-center justify-between gap-2 text-xs text-slate-300">
        <div className="flex flex-col">
          <span className="font-semibold text-white text-[11px]">Chrome Shortcuts Manager</span>
          <span className="text-[10px] text-slate-400">Bind Alt + 1, Alt + 2 to switch slots globally</span>
        </div>
        <button
          className="bg-sky-600 hover:bg-sky-500 text-white font-semibold text-[10px] px-2.5 py-1 rounded transition-all shrink-0 shadow-sm"
          onClick={() => chrome.tabs.create({ url: 'chrome://extensions/shortcuts' })}
        >
          Open Shortcuts ↗
        </button>
      </div>

      {/* Footer */}
      <footer className="pt-2 border-t border-white/10 flex items-center justify-between text-[10px] text-slate-400">
        <span>Press shortcut or click <strong>Switch</strong></span>
        <span className="font-mono">v0.1.0</span>
      </footer>

      {/* Shortcut Configuration Modal */}
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
