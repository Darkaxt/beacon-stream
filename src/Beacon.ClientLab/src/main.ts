import './style.css';
import {
  createBenchmarkCompletionPayload,
  createBenchmarkPreparePayload,
  createCapabilitiesPayload,
  createGamePlanRequest,
  createKeyboardInputPayload,
  createPointerGesturePayload,
  createTelemetryPayload,
  ClientPresence,
  leaveAndQuit,
  formatBeaconState,
  formatLaunchEvents,
  formatInputAccepted,
  formatPlanDetails,
  formatStreamStop,
  getJson,
  postJson,
  type GameDescriptor,
  type GameLibrarySnapshot,
  type BeaconResponse,
  type BenchmarkNetworkProfileName,
  type BenchmarkPrepareResponse,
  type BenchmarkTrigger,
  type InputAcceptedResponse,
  type LaunchResponse,
  type PlanResponse,
  type PlanRequest,
  type StreamStopResponse
} from './clientLab';

const clientId = 'z-fold-7';
const benchmarkNetworkInput = select('benchmarkNetworkInput');
const gameSelect = select('gameSelect');
const gameCover = element('gameCover');
const gameTitle = element('gameTitle');
const gameMeta = element('gameMeta');
const gameDiagnostics = element('gameDiagnostics');
const eventLog = element('eventLog');
let games: GameDescriptor[] = [];

const presence = new ClientPresence(async active => {
  const result = await postJson<BeaconResponse>(
    `/clients/${clientId}/beacon`,
    { active },
    !active);
  appendLog(formatBeaconState(result));
});

window.addEventListener('pagehide', () => {
  void presence.leave();
});

element('helloButton').addEventListener('click', async () => {
  let result: { clientId: string } | undefined;
  const active = await presence.enter(async () => {
    result = await postJson<{ clientId: string }>('/clients/hello', { clientId, name: 'Z Fold 7' });
    await postJson(`/clients/${clientId}/capabilities`, createCapabilitiesPayload());
  });
  if (!active || result === undefined) return;
  await loadGames();
  await runBenchmark('automatic', false);
  appendLog(`hello ${result.clientId}`);
});

benchmarkNetworkInput.addEventListener('change', async () => {
  if (presence.isActive()) {
    await runBenchmark('automatic');
  }
});

gameSelect.addEventListener('change', () => {
  updateGameSummary();
});

element('automaticBenchmarkButton').addEventListener('click', async () => {
  await runBenchmark('automatic');
});

element('manualBenchmarkButton').addEventListener('click', async () => {
  await runBenchmark('manual');
});

element('planButton').addEventListener('click', async () => {
  await submitClientFacts();
  const plan = await postJson<PlanResponse>(`/clients/${clientId}/plan`, createPlanRequest());
  appendLog(formatPlanDetails(plan));
});

element('launchButton').addEventListener('click', async () => {
  await submitClientFacts();
  await runBenchmark('sessionPreflight', false);
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

element('disconnectButton').addEventListener('click', async () => {
  const result = await postJson<{ leaseRetained: boolean }>(`/clients/${clientId}/disconnect`, {});
  appendLog(result.leaseRetained ? 'lease retained' : 'lease released');
});

element('reconnectButton').addEventListener('click', async () => {
  const result = await postJson<{ displayId: string; state: string }>(`/clients/${clientId}/reconnect`, {});
  appendLog(`${result.state} ${result.displayId}`);
});

element('stopStreamButton').addEventListener('click', async () => {
  const result = await postJson<StreamStopResponse>(`/clients/${clientId}/stream/stop`, {});
  appendLog(formatStreamStop(result));
});

element('quitButton').addEventListener('click', async () => {
  const outcome = await leaveAndQuit(presence, () =>
    postJson<{ cleanupEvaluated: boolean; displayRemoved: boolean }>(`/clients/${clientId}/quit`, {
      clientActive: false
    }));
  if (outcome.result !== undefined) {
    appendLog(`${outcome.result.cleanupEvaluated ? 'cleanup evaluated' : 'cleanup skipped'} ${outcome.result.displayRemoved ? 'display removed' : 'display retained'}`);
  }
  for (const failure of outcome.failures) {
    appendLog(`${failure.step} failed: ${failure.message}`);
  }
});

element('restoreButton').addEventListener('click', async () => {
  const result = await postJson<{ displayId: string; recovered: boolean }>(`/clients/${clientId}/emergency-restore`, {});
  appendLog(result.recovered ? `recovered ${result.displayId}` : 'recovery skipped');
});

function createPlanRequest(): PlanRequest {
  return createGamePlanRequest(gameSelect.value);
}

async function submitClientFacts(): Promise<void> {
  await postJson(`/clients/${clientId}/capabilities`, createCapabilitiesPayload());
  await postJson(`/clients/${clientId}/telemetry`, createTelemetryPayload());
  appendLog('facts reported');
}

async function runBenchmark(
  trigger: BenchmarkTrigger,
  reportCapabilities = true
): Promise<void> {
  if (reportCapabilities) {
    await postJson(`/clients/${clientId}/capabilities`, createCapabilitiesPayload());
  }
  const prepared = await postJson<BenchmarkPrepareResponse>(
    `/clients/${clientId}/benchmarks/prepare`,
    createBenchmarkPreparePayload(
      trigger,
      benchmarkNetworkInput.value as BenchmarkNetworkProfileName));
  appendLog(`${trigger} benchmark ${prepared.disposition}`);
  if (prepared.disposition === 'reuse') return;

  await postJson(
    `/clients/${clientId}/benchmarks/${prepared.runId}/complete`,
    createBenchmarkCompletionPayload(prepared));
  appendLog(`${trigger} benchmark complete ${prepared.runId}`);
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

function select(id: string): HTMLSelectElement {
  return element(id) as HTMLSelectElement;
}
