import './style.css';
import { createDefaultProfile, patchJson, postJson, validateProfileDraft, type HdrPreference, type ProfileDraft } from './clientLab';

const clientId = 'z-fold-7';
const widthInput = input('widthInput');
const heightInput = input('heightInput');
const refreshInput = input('refreshInput');
const hdrInput = select('hdrInput');
const codecInput = select('codecInput');
const bitrateInput = input('bitrateInput');
const profileError = element('profileError');
const eventLog = element('eventLog');

setDraft(createDefaultProfile());

element('helloButton').addEventListener('click', async () => {
  const result = await postJson<{ clientId: string }>('/clients/hello', { clientId, name: 'Z Fold 7' });
  appendLog(`hello ${result.clientId}`);
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
  const plan = await postJson<PlanResponse>(`/clients/${clientId}/plan`, createPlanRequest());
  appendLog(`${plan.display.mode} ${plan.display.width}x${plan.display.height}@${plan.display.refreshHz} ${plan.stream.codec} ${plan.stream.fps}fps`);
});

element('launchButton').addEventListener('click', async () => {
  const launch = await postJson<{ displayId: string; state: string }>(`/clients/${clientId}/launch`, createPlanRequest());
  appendLog(`${launch.state} ${launch.displayId}`);
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
    clientActive: false,
    ownedProcessRunning: false,
    ownedWindowRemaining: false
  });
  appendLog(`${result.cleanupEvaluated ? 'cleanup evaluated' : 'cleanup skipped'} ${result.displayRemoved ? 'display removed' : 'display retained'}`);
});

element('restoreButton').addEventListener('click', async () => {
  const result = await postJson<{ restoreRequested: boolean }>(`/clients/${clientId}/emergency-restore`, {});
  appendLog(result.restoreRequested ? 'restore requested' : 'restore skipped');
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

function createPlanRequest(): { appId: string; title: string; source: string } {
  return {
    appId: 'steam-shortcut:3767414131',
    title: 'Dispatch',
    source: 'steam-shortcut'
  };
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

interface PlanResponse {
  display: {
    mode: string;
    width: number;
    height: number;
    refreshHz: number;
  };
  stream: {
    codec: string;
    fps: number;
  };
}
