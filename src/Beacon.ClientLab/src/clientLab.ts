export type HdrPreference = 'Off' | 'Prefer' | 'Require';
export type TelemetryProfileName = 'excellent-lan' | 'congested-lan' | 'high-rtt' | 'packet-loss' | 'low-bitrate-cap' | 'thermal-battery';
export type BenchmarkTrigger = 'automatic' | 'manual' | 'sessionPreflight';
export type BenchmarkNetworkProfileName = 'home-wifi' | 'mobile-hotspot';

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
}

export interface PlanResponse {
  display: {
    mode: string;
    width: number;
    height: number;
    refreshHz: number;
    reason: string;
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

export interface StreamStopResponse {
  clientId: string;
  stream: StreamState | null;
}

export interface ClientDisplayMode {
  width: number;
  height: number;
  refreshHz: number;
}

export interface CapabilitiesPayload {
  av1: boolean;
  hevc: boolean;
  h264: boolean;
  hdr10: boolean;
  virtualDisplayHdrSupported: boolean;
  maxFps: number;
  lowLatencyDecode: boolean;
  currentDisplayMode: ClientDisplayMode;
  supportedDisplayModes: ClientDisplayMode[];
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

export interface InputPayload {
  sequence: number;
  events: InputEventPayload[];
}

export interface InputEventPayload {
  type: string;
  action: string;
  pointerId?: number;
  x?: number;
  y?: number;
  buttons?: number;
  key?: string;
  code?: string;
  value?: number;
}

export interface InputAcceptedResponse {
  accepted: boolean;
  eventCount: number;
  sessionId: string;
}

export interface BeaconResponse {
  state: string;
  displayId: string;
  leasePrepared: boolean;
  displayRemoved: boolean;
}

export interface BenchmarkPreparePayload {
  trigger: BenchmarkTrigger;
  fingerprints: {
    network: {
      schemaVersion: number;
      serverRoute: string;
      transport: string;
      localNetworkPrefix: string;
      wifiBand: string;
      wifiChannel: number;
      linkSpeedBucket: string;
      saltedNetworkIdHash: string;
    };
    hardware: {
      schemaVersion: number;
      deviceCapabilityRevision: string;
      androidVersion: string;
      apkVersion: string;
      displayModeInventoryRevision: string;
      codecInventoryRevision: string;
    };
  };
}

export interface BenchmarkDecoderRound {
  codec: string;
  profile: string;
  bitDepth: number;
  width: number;
  height: number;
  targetFps: number;
}

export interface BenchmarkPrepareResponse {
  disposition: 'start-new' | 'continue' | 'reuse';
  runId: string;
  networkCoverage: { firstSequence: number; expectedPacketCount: number };
  transportPlan: { datagramPayloadBytes: number };
  hardwarePlan: { decoderRounds: BenchmarkDecoderRound[] };
}

export interface BenchmarkCompletionPayload {
  networkSamples: Array<{
    sequence: number;
    payloadBytes: number;
    rttMs: number;
    jitterMs: number;
    received: boolean;
    throughputMbps: number;
    reorderDistance: number;
  }>;
  decoderSamples: Array<BenchmarkDecoderRound & {
    configured: boolean;
    sustainedFps: number;
    p95DecodeLatencyMs: number;
    p95PresentationLatencyMs: number;
    droppedFrames: number;
    outputErrors: number;
    tenBitPresentationVerified: boolean;
    hdrPresentationVerified: boolean;
  }>;
  powerSamples: Array<{
    batteryPercent: number;
    isCharging: boolean;
    thermalState: string;
  }>;
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
  const currentDisplayMode = { width: 2560, height: 1600, refreshHz: 120 };

  return {
    av1: true,
    hevc: true,
    h264: true,
    hdr10: true,
    virtualDisplayHdrSupported: false,
    maxFps: 120,
    lowLatencyDecode: true,
    currentDisplayMode,
    supportedDisplayModes: [currentDisplayMode]
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

export function createBenchmarkPreparePayload(
  trigger: BenchmarkTrigger,
  networkProfile: BenchmarkNetworkProfileName
): BenchmarkPreparePayload {
  const network = networkProfile === 'mobile-hotspot'
    ? {
        schemaVersion: 3,
        serverRoute: '192.168.43.1',
        transport: 'wifi',
        localNetworkPrefix: '192.168.43.0/24',
        wifiBand: '5-ghz',
        wifiChannel: 149,
        linkSpeedBucket: '100-499-mbps',
        saltedNetworkIdHash: 'b'.repeat(64)
      }
    : {
        schemaVersion: 3,
        serverRoute: '192.168.1.10',
        transport: 'wifi',
        localNetworkPrefix: '192.168.1.0/24',
        wifiBand: '6-ghz',
        wifiChannel: 37,
        linkSpeedBucket: '500-999-mbps',
        saltedNetworkIdHash: 'a'.repeat(64)
      };
  return {
    trigger,
    fingerprints: {
      network,
      hardware: {
        schemaVersion: 3,
        deviceCapabilityRevision: 'client-lab-z-fold-7-v1',
        androidVersion: '16',
        apkVersion: 'client-lab-0.1.0',
        displayModeInventoryRevision: '2560x1600-120',
        codecInventoryRevision: 'av1-hevc-h264'
      }
    }
  };
}

export function createBenchmarkCompletionPayload(
  prepared: BenchmarkPrepareResponse
): BenchmarkCompletionPayload {
  const networkSamples = Array.from(
    { length: prepared.networkCoverage.expectedPacketCount },
    (_, index) => ({
      sequence: prepared.networkCoverage.firstSequence + index,
      payloadBytes: prepared.transportPlan.datagramPayloadBytes,
      rttMs: 8,
      jitterMs: 1,
      received: true,
      throughputMbps: 100,
      reorderDistance: 0
    })
  );
  const decoderSamples = prepared.hardwarePlan.decoderRounds.map(round => ({
    ...round,
    configured: true,
    sustainedFps: round.targetFps,
    p95DecodeLatencyMs: 1,
    p95PresentationLatencyMs: 2,
    droppedFrames: 0,
    outputErrors: 0,
    tenBitPresentationVerified: round.bitDepth === 10,
    hdrPresentationVerified: false
  }));
  const powerSamples = prepared.hardwarePlan.decoderRounds.flatMap(() => [
    { batteryPercent: 100, isCharging: true, thermalState: 'nominal' },
    { batteryPercent: 100, isCharging: true, thermalState: 'nominal' }
  ]);
  if (powerSamples.length === 0) {
    powerSamples.push({ batteryPercent: 100, isCharging: true, thermalState: 'nominal' });
  }
  return { networkSamples, decoderSamples, powerSamples };
}

export function formatPlanDetails(plan: PlanResponse): string {
  return `${plan.display.mode} ${plan.display.width}x${plan.display.height}@${plan.display.refreshHz} ${plan.stream.codec} ${plan.stream.fps}fps ${plan.stream.initialBitrateMbps}Mbps ${plan.stream.transport}/${plan.stream.congestionPolicy} - ${plan.display.reason} ${plan.stream.reason}`;
}

export function formatLaunchEvents(launch: LaunchResponse): string[] {
  const events = [`${launch.state} ${launch.displayId}`];
  if (launch.stream !== null) {
    events.push(`${launch.stream.state} ${launch.stream.codec} ${launch.stream.fps}fps`);
  }

  return events;
}

export function formatStreamStop(response: StreamStopResponse): string {
  return response.stream === null
    ? `no active stream for ${response.clientId}`
    : `${response.stream.state} ${response.stream.sessionId}`;
}

export function createPointerGesturePayload(sequence: number): InputPayload {
  return {
    sequence,
    events: [
      {
        type: 'pointer',
        action: 'down',
        pointerId: 1,
        x: 0.5,
        y: 0.5,
        buttons: 1
      },
      {
        type: 'pointer',
        action: 'move',
        pointerId: 1,
        x: 0.75,
        y: 0.25
      },
      {
        type: 'pointer',
        action: 'up',
        pointerId: 1,
        x: 0.75,
        y: 0.25,
        buttons: 1
      }
    ]
  };
}

export function createKeyboardInputPayload(sequence: number): InputPayload {
  return {
    sequence,
    events: [
      {
        type: 'keyboard',
        action: 'press',
        key: 'Escape',
        code: 'Escape'
      }
    ]
  };
}

export function formatInputAccepted(response: InputAcceptedResponse): string {
  return `input ${response.accepted ? 'accepted' : 'rejected'} ${response.eventCount} event(s) ${response.sessionId}`;
}

export function formatBeaconState(response: BeaconResponse): string {
  if (response.state === 'active') {
    return `beacon active ${response.displayId} ${response.leasePrepared ? 'prepared' : 'not prepared'}`;
  }

  return `beacon inactive ${response.displayId} ${response.displayRemoved ? 'removed' : 'retained'}`;
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
