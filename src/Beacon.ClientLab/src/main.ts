import './style.css';
import {
  createCapabilitiesPayload,
  createDefaultProfile,
  createGamePlanRequest,
  createKeyboardInputPayload,
  createPointerGesturePayload,
  createTelemetryPayload,
  formatBeaconState,
  formatLaunchEvents,
  formatInputAccepted,
  formatPlanDetails,
  getJson,
  patchJson,
  postJson,
  validateProfileDraft,
  type GameDescriptor,
  type GameLibrarySnapshot,
  type BeaconResponse,
  type HdrPreference,
  type InputAcceptedResponse,
  type LaunchResponse,
  type PlanResponse,
  type PlanRequest,
  type ProfileDraft,
  type TelemetryProfileName
} from './clientLab';

const clientId = 'z-fold-7';
const widthInput = input('widthInput');
const heightInput = input('heightInput');
const refreshInput = input('refreshInput');
const hdrInput = select('hdrInput');
const codecInput = select('codecInput');
const bitrateInput = input('bitrateInput');
const telemetryProfileInput = select('telemetryProfileInput');
const gameSelect = select('gameSelect');
const gameCover = element('gameCover');
const gameTitle = element('gameTitle');
const gameMeta = element('gameMeta');
const gameDiagnostics = element('gameDiagnostics');
const profileError = element('profileError');
const eventLog = element('eventLog');
let games: GameDescriptor[] = [];

setDraft(createDefaultProfile());

element('helloButton').addEventListener('click', async () => {
  const result = await postJson<{ clientId: string }>('/clients/hello', { clientId, name: 'Z Fold 7' });
  await loadGames();
  appendLog(`hello ${result.clientId}`);
});

gameSelect.addEventListener('change', () => {
  updateGameSummary();
});

element('profileForm').addEventListener('submit', async event => {
  event.preventDefault();
  const draft = readDraft();
  const validation = validateProfileDraft(draft);
  profileError.textContent = validation.message;
  if (!validation.ok) {
    return;
  }

  await patchJson(`/clients/${clientId}/profile`, {
    preferredWidth: draft.preferredWidth,
    preferredHeight: draft.preferredHeight,
    preferredRefreshHz: draft.preferredRefreshHz,
    hdrPreference: draft.hdrPreference,
    codecPreference: draft.codecPreference,
    bitrateCapMbps: draft.bitrateCapMbps
  });
  appendLog('profile saved');
});

element('planButton').addEventListener('click', async () => {
  await submitClientFacts();
  const plan = await postJson<PlanResponse>(`/clients/${clientId}/plan`, createPlanRequest());
  appendLog(formatPlanDetails(plan));
});

element('launchButton').addEventListener('click', async () => {
  await submitClientFacts();
  const launch = await postJson<LaunchResponse>(`/clients/${clientId}/launch`, createPlanRequest());
  for (const message of formatLaunchEvents(launch)) {
    appendLog(message);
  }
});

element('inputButton').addEventListener('click', async () => {
  const result = await postJson<InputAcceptedResponse>(`/clients/${clientId}/input`, createPointerGesturePayload(1));
  appendLog(formatInputAccepted(result));
});

element('keyboardButton').addEventListener('click', async () => {
  const result = await postJson<InputAcceptedResponse>(`/clients/${clientId}/input`, createKeyboardInputPayload(2));
  appendLog(formatInputAccepted(result));
});

element('activeBeaconButton').addEventListener('click', async () => {
  const result = await postJson<BeaconResponse>(`/clients/${clientId}/beacon`, { active: true });
  appendLog(formatBeaconState(result));
});

element('inactiveBeaconButton').addEventListener('click', async () => {
  const result = await postJson<BeaconResponse>(`/clients/${clientId}/beacon`, { active: false });
  appendLog(formatBeaconState(result));
});

element('disconnectButton').addEventListener('click', async () => {
  const result = await postJson<{ leaseRetained: boolean }>(`/clients/${clientId}/disconnect`, {});
  appendLog(result.leaseRetained ? 'lease retained' : 'lease released');
});

element('reconnectButton').addEventListener('click', async () => {
  const result = await postJson<{ displayId: string; state: string }>(`/clients/${clientId}/reconnect`, {});
  appendLog(`${result.state} ${result.displayId}`);
});

element('quitButton').addEventListener('click', async () => {
  const result = await postJson<{ cleanupEvaluated: boolean; displayRemoved: boolean }>(`/clients/${clientId}/quit`, {
    clientActive: false
  });
  appendLog(`${result.cleanupEvaluated ? 'cleanup evaluated' : 'cleanup skipped'} ${result.displayRemoved ? 'display removed' : 'display retained'}`);
});

element('restoreButton').addEventListener('click', async () => {
  const result = await postJson<{ displayId: string; recovered: boolean }>(`/clients/${clientId}/emergency-restore`, {});
  appendLog(result.recovered ? `recovered ${result.displayId}` : 'recovery skipped');
});

function readDraft(): ProfileDraft {
  return {
    clientId,
    preferredWidth: widthInput.valueAsNumber,
    preferredHeight: heightInput.valueAsNumber,
    preferredRefreshHz: refreshInput.valueAsNumber,
    hdrPreference: hdrInput.value as HdrPreference,
    codecPreference: codecInput.value,
    bitrateCapMbps: bitrateInput.value === '' ? null : bitrateInput.valueAsNumber
  };
}

function setDraft(profile: ProfileDraft): void {
  widthInput.value = String(profile.preferredWidth);
  heightInput.value = String(profile.preferredHeight);
  refreshInput.value = String(profile.preferredRefreshHz);
  hdrInput.value = profile.hdrPreference;
  codecInput.value = profile.codecPreference;
  bitrateInput.value = profile.bitrateCapMbps === null ? '' : String(profile.bitrateCapMbps);
}

function createPlanRequest(): PlanRequest {
  return createGamePlanRequest(gameSelect.value);
}

async function submitClientFacts(): Promise<void> {
  await postJson(`/clients/${clientId}/capabilities`, createCapabilitiesPayload());
  await postJson(`/clients/${clientId}/telemetry`, createTelemetryPayload(readTelemetryProfile()));
  appendLog(`facts ${telemetryProfileInput.value}`);
}

function readTelemetryProfile(): TelemetryProfileName {
  return telemetryProfileInput.value as TelemetryProfileName;
}

async function loadGames(): Promise<void> {
  const snapshot = await getJson<GameLibrarySnapshot>('/games');
  games = snapshot.games;
  gameSelect.replaceChildren();

  if (games.length === 0) {
    gameSelect.append(new Option('Dispatch fallback', ''));
  } else {
    for (const game of games) {
      gameSelect.append(new Option(`${game.title} (${game.source})`, game.id));
    }
  }

  gameDiagnostics.textContent = snapshot.diagnostics.length === 0 ? '' : `${snapshot.diagnostics.length} diagnostic(s)`;
  updateGameSummary();
}

function updateGameSummary(): void {
  const selected = games.find(game => game.id === gameSelect.value);
  if (!selected) {
    setGameCover(null, 'Dispatch');
    gameTitle.textContent = 'Dispatch';
    gameMeta.textContent = 'steam-shortcut | fallback request';
    return;
  }

  setGameCover(selected.artwork.coverPath, selected.title);
  gameTitle.textContent = selected.title;
  gameMeta.textContent = `${selected.source} | ${selected.installed ? 'installed' : 'not installed'} | ${selected.launch.type}`;
}

function setGameCover(coverPath: string | null, title: string): void {
  gameCover.textContent = createInitials(title);
  gameCover.style.backgroundImage = '';

  if (coverPath !== null && coverPath.trim() !== '') {
    gameCover.textContent = '';
    gameCover.style.backgroundImage = `url("${coverPath}")`;
  }
}

function createInitials(title: string): string {
  return title
    .split(/\s+/)
    .filter(part => part.length > 0)
    .slice(0, 2)
    .map(part => part[0].toUpperCase())
    .join('');
}

function appendLog(message: string): void {
  const line = document.createElement('p');
  line.textContent = message;
  eventLog.prepend(line);
}

function element(id: string): HTMLElement {
  const found = document.getElementById(id);
  if (!found) {
    throw new Error(`Missing element ${id}`);
  }

  return found;
}

function input(id: string): HTMLInputElement {
  return element(id) as HTMLInputElement;
}

function select(id: string): HTMLSelectElement {
  return element(id) as HTMLSelectElement;
}
