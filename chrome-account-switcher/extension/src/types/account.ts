// Profile and Slot type definitions

export interface ProfileSlotConfig {
  slot: number;
  profileDirectory: string; // e.g. "Default", "Profile 1", "Profile 7"
  displayName?: string;
  email?: string;
}

export interface TabInfo {
  url: string;
  title?: string;
  active: boolean;
  index?: number;
}

export interface NativeSwitchRequest {
  action: 'switch-profile';
  slot?: number;
  profileDirectory?: string;
  copyTabs?: boolean;
  sourceProfile?: string;
  tabs?: TabInfo[];
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
}

export interface NativeGetProfilesResponse {
  success: boolean;
  profiles?: Array<{
    directoryName: string;
    displayName: string;
    email?: string;
  }>;
  slots?: ProfileSlotConfig[];
  error?: string;
}
