export interface DiscoveredProfile {
  directory: string;
  displayName: string;
  gaiaName?: string;
  email?: string;
  avatarIcon?: string;
  orderIndex: number;
  isCurrent?: boolean;
  shortcut?: string;
}

export interface ProfileSlotConfig {
  slot: number;
  profileDirectory: string; // e.g. "Default", "Profile 1", "Profile 7"
  displayName?: string;
  gaiaName?: string;
  email?: string;
  avatarIcon?: string;
  shortcut?: string; // e.g. "Alt + Shift + 1", "Ctrl + Alt + 3", "Alt + 2"
  isCurrent?: boolean;
}

export interface TabInfo {
  url: string;
  title?: string;
  active: boolean;
  index?: number;
}

export interface NativeSwitchRequest {
  action: 'switch-profile' | 'get-slots' | 'set-shortcut' | 'sync-slots' | 'ping' | 'getProfiles' | 'get-profiles';
  slot?: number;
  profileDirectory?: string;
  copyTabs?: boolean;
  sourceProfile?: string;
  tabs?: TabInfo[];
  shortcut?: string;
  slots?: ProfileSlotConfig[];
}

export interface NativeSwitchResponse {
  success: boolean;
  profile?: string;
  displayName?: string;
  sourceProfile?: string;
  targetProfile?: string;
  tabsCopied?: number;
  tabsSkipped?: number;
  windowHandle?: number;
  error?: string;
  message?: string;
  slots?: ProfileSlotConfig[];
  profiles?: DiscoveredProfile[];
  currentProfile?: string;
}
