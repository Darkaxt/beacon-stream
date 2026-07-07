export type HdrPreference = 'Off' | 'Prefer' | 'Require';

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

export interface LaunchResponse {
  clientId: string;
  displayId: string;
  state: string;
  stream: StreamState | null;
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

export function formatLaunchEvents(launch: LaunchResponse): string[] {
  const events = [`${launch.state} ${launch.displayId}`];
  if (launch.stream !== null) {
    events.push(`${launch.stream.state} ${launch.stream.codec} ${launch.stream.fps}fps`);
  }

  return events;
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
