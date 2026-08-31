// Profile and Slot type definitions

export interface ProfileSlotConfig {
  slot: number;
  profileDirectory: string; // e.g. "Default", "Profile 1", "Profile 7"
  displayName?: string;
  email?: string;
}

export interface NativeSwitchRequest {
  action: 'switch-profile';
  slot?: number;
  profileDirectory?: string;
}

export interface NativeSwitchResponse {
  success: boolean;
  profile?: string;
  displayName?: string;
  windowHandle?: number;
  error?: string;
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
