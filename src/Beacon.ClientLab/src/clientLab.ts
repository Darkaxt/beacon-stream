export type HdrPreference = 'Off' | 'Prefer' | 'Require';
export type TelemetryProfileName = 'excellent-lan' | 'congested-lan' | 'high-rtt' | 'packet-loss' | 'low-bitrate-cap' | 'thermal-battery';

export const telemetryProfileNames: TelemetryProfileName[] = [
  'excellent-lan',
  'congested-lan',
  'high-rtt',
  'packet-loss',
  'low-bitrate-cap',
  'thermal-battery'
];

export interface ProfileDraft {
  clientId: string;
  preferredWidth: number;
  preferredHeight: number;
  preferredRefreshHz: number;
  hdrPreference: HdrPreference;
  codecPreference: string;
  bitrateCapMbps: number | null;
}

export interface ValidationResult {
  ok: boolean;
  message: string;
}

export interface GameLibrarySnapshot {
  games: GameDescriptor[];
  diagnostics: string[];
}

export interface GameDescriptor {
  id: string;
  title: string;
  source: string;
  launch: GameLaunchIntent;
  artwork: GameArtwork;
  installed: boolean;
}

export interface GameLaunchIntent {
  type: string;
  command: string;
}

export interface GameArtwork {
  coverPath: string | null;
  source: string;
}

export interface PlanRequest {
  appId?: string;
  title?: string;
  source?: string;
  gameId?: string;
}

export interface StreamConnectionEndpoint {
  role: string;
  uri: string;
}

export interface StreamConnection {
  protocol: string;
  launchUri: string | null;
  endpoints: StreamConnectionEndpoint[];
  metadata: Record<string, string>;
}

export interface StreamState {
  sessionId: string;
  clientId: string;
  appId: string;
  displayId: string;
  codec: string;
  fps: number;
  initialBitrateMbps: number;
  transport: string;
  state: string;
  error: string | null;
  connection: StreamConnection | null;
}

export interface PlanResponse {
  display: {
    mode: string;
    width: number;
    height: number;
    refreshHz: number;
  };
  stream: {
    codec: string;
    fps: number;
    initialBitrateMbps: number;
    transport: string;
    congestionPolicy: string;
    reason: string;
  };
}

export interface LaunchResponse {
  clientId: string;
  displayId: string;
  state: string;
  stream: StreamState | null;
}

export interface CapabilitiesPayload {
  av1: boolean;
  hevc: boolean;
  h264: boolean;
  hdr10: boolean;
  virtualDisplayHdrSupported: boolean;
  maxFps: number;
  lowLatencyDecode: boolean;
  currentScreenMode: string;
}

export interface TelemetryPayload {
  rttMs: number;
  packetLossPercent: number;
  decoderLoadPercent: number;
  estimatedBandwidthMbps: number;
  wifiBand: string;
  batteryPercent: number;
  thermalState: string;
}

export function createDefaultProfile(): ProfileDraft {
  return {
    clientId: 'z-fold-7',
    preferredWidth: 2560,
    preferredHeight: 1600,
    preferredRefreshHz: 120,
    hdrPreference: 'Prefer',
    codecPreference: 'auto',
    bitrateCapMbps: null
  };
}

export function validateProfileDraft(profile: ProfileDraft): ValidationResult {
  if (profile.clientId === 'z-fold-7' && profile.preferredWidth === 2560 && profile.preferredHeight === 1440) {
    return {
      ok: false,
      message: '2560x1440 is blocked for Z Fold 7; keep the 16:10 virtual desktop intent.'
    };
  }

  return { ok: true, message: '' };
}

export function createGamePlanRequest(selectedGameId: string): PlanRequest {
  const gameId = selectedGameId.trim();
  if (gameId !== '') {
    return { gameId };
  }

  return {
    appId: 'steam-shortcut:3767414131',
    title: 'Dispatch',
    source: 'steam-shortcut'
  };
}

export function createCapabilitiesPayload(): CapabilitiesPayload {
  return {
    av1: true,
    hevc: true,
    h264: true,
    hdr10: true,
    virtualDisplayHdrSupported: false,
    maxFps: 120,
    lowLatencyDecode: true,
    currentScreenMode: '2560x1600@120'
  };
}

export function createTelemetryPayload(profile: TelemetryProfileName): TelemetryPayload {
  switch (profile) {
    case 'congested-lan':
      return createTelemetry(55, 1.5, 55, 45, 'wifi-6', 60, 'nominal');
    case 'high-rtt':
      return createTelemetry(115, 0.5, 35, 80, 'wifi-5', 70, 'nominal');
    case 'packet-loss':
      return createTelemetry(22, 3.2, 40, 90, 'wifi-6', 70, 'nominal');
    case 'low-bitrate-cap':
      return createTelemetry(8, 0, 30, 35, 'wifi-6', 75, 'nominal');
    case 'thermal-battery':
      return createTelemetry(12, 0, 88, 100, 'wifi-6', 9, 'hot');
    case 'excellent-lan':
      return createTelemetry(8, 0, 20, 120, 'wifi-7', 80, 'nominal');
  }
}

export function formatPlanDetails(plan: PlanResponse): string {
  return `${plan.display.mode} ${plan.display.width}x${plan.display.height}@${plan.display.refreshHz} ${plan.stream.codec} ${plan.stream.fps}fps ${plan.stream.initialBitrateMbps}Mbps ${plan.stream.transport}/${plan.stream.congestionPolicy} - ${plan.stream.reason}`;
}

export function formatLaunchEvents(launch: LaunchResponse): string[] {
  const events = [`${launch.state} ${launch.displayId}`];
  if (launch.stream !== null) {
    events.push(`${launch.stream.state} ${launch.stream.codec} ${launch.stream.fps}fps`);
    if (launch.stream.connection?.launchUri) {
      events.push(launch.stream.connection.launchUri);
    }
  }

  return events;
}

function createTelemetry(
  rttMs: number,
  packetLossPercent: number,
  decoderLoadPercent: number,
  estimatedBandwidthMbps: number,
  wifiBand: string,
  batteryPercent: number,
  thermalState: string
): TelemetryPayload {
  return {
    rttMs,
    packetLossPercent,
    decoderLoadPercent,
    estimatedBandwidthMbps,
    wifiBand,
    batteryPercent,
    thermalState
  };
}

export async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(path);
  return readJson<T>(response);
}

export async function postJson<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });

  return readJson<T>(response);
}

export async function patchJson<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(path, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });

  return readJson<T>(response);
}

async function readJson<T>(response: Response): Promise<T> {
  const payload = await response.json();
  if (!response.ok) {
    throw new Error(payload.error ?? `Request failed with ${response.status}`);
  }

  return payload as T;
}
