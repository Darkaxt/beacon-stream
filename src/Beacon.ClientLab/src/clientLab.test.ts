import { describe, expect, it } from 'vitest';
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
  type LaunchResponse,
  type BenchmarkPrepareResponse,
  type PlanResponse
} from './clientLab';

describe('Client Lab thin client', () => {
  it('reports structured current and supported display mode facts', () => {
    const payload = createCapabilitiesPayload();

    expect(payload.currentDisplayMode).toEqual({
      width: 2560,
      height: 1600,
      refreshHz: 120
    });
    expect(payload.supportedDisplayModes).toEqual([
      { width: 2560, height: 1600, refreshHz: 120 }
    ]);
    expect(payload).not.toHaveProperty('currentScreenMode');
  });

  it('announces active and inactive once across duplicate lifecycle callbacks', async () => {
    const transitions: boolean[] = [];
    const presence = new ClientPresence(async active => {
      transitions.push(active);
    });

    await presence.enter();
    await presence.enter();
    await presence.leave();
    await presence.leave();

    expect(transitions).toEqual([true, false]);
  });

  it('retries a failed inactive transition on the next departure callback', async () => {
    const transitions: boolean[] = [];
    let failInactive = true;
    const presence = new ClientPresence(async active => {
      transitions.push(active);
      if (!active && failInactive) {
        failInactive = false;
        throw new Error('inactive unavailable');
      }
    });

    await presence.enter();
    await expect(presence.leave()).rejects.toThrow('inactive unavailable');
    await presence.leave();

    expect(transitions).toEqual([true, false, false]);
  });

  it('treats a lost active response as possibly active until departure succeeds', async () => {
    const transitions: boolean[] = [];
    let loseActiveResponse = true;
    const presence = new ClientPresence(async active => {
      transitions.push(active);
      if (active && loseActiveResponse) {
        loseActiveResponse = false;
        throw new Error('active response lost');
      }
    });

    await expect(presence.enter()).rejects.toThrow('active response lost');
    await presence.leave();

    expect(transitions).toEqual([true, false]);
  });

  it('attempts quit and reports both results when inactive fails', async () => {
    let quitRequests = 0;
    const presence = new ClientPresence(async active => {
      if (!active) throw new Error('inactive unavailable');
    });
    await presence.enter();

    const outcome = await leaveAndQuit(presence, async () => {
      quitRequests++;
      return { cleanupEvaluated: true, displayRemoved: true };
    });

    expect(quitRequests).toBe(1);
    expect(outcome.result).toEqual({ cleanupEvaluated: true, displayRemoved: true });
    expect(outcome.failures).toEqual([
      { step: 'inactive', message: 'inactive unavailable' }
    ]);
  });

  it('does not announce active when departure occurs during asynchronous setup', async () => {
    const transitions: boolean[] = [];
    let releaseSetup: (() => void) | undefined;
    const setupBlocked = new Promise<void>(resolve => {
      releaseSetup = resolve;
    });
    let setupStarted: (() => void) | undefined;
    const started = new Promise<void>(resolve => {
      setupStarted = resolve;
    });
    const presence = new ClientPresence(async active => {
      transitions.push(active);
    });

    const entering = presence.enter(async () => {
      setupStarted?.();
      await setupBlocked;
    });
    await started;
    const leaving = presence.leave();
    releaseSetup?.();

    expect(await entering).toBe(false);
    await leaving;
    expect(transitions).toEqual([]);
  });

  it('plans by normalized game id when a catalog entry is selected', () => {
    const request = createGamePlanRequest('steam-shortcut:3767414131');

    expect(request).toEqual({ gameId: 'steam-shortcut:3767414131' });
  });

  it('keeps the manual Dispatch fallback when no catalog entry is selected', () => {
    const request = createGamePlanRequest('');

    expect(request).toEqual({
      appId: 'steam-shortcut:3767414131',
      title: 'Dispatch',
      source: 'steam-shortcut'
    });
  });

  it('formats streaming launch state with backend details', () => {
    const launch: LaunchResponse = {
      clientId: 'z-fold-7',
      displayId: 'client-z-fold-7',
      state: 'streaming',
      stream: {
        sessionId: 'z-fold-7-steam-shortcut:3767414131',
        clientId: 'z-fold-7',
        appId: 'steam-shortcut:3767414131',
        displayId: 'client-z-fold-7',
        codec: 'av1',
        fps: 120,
        initialBitrateMbps: 65,
        transport: 'lan-direct',
        state: 'running',
        error: null
      }
    };

    expect(formatLaunchEvents(launch)).toEqual([
      'streaming client-z-fold-7',
      'running av1 120fps'
    ]);
  });

  it('formats explicit stream stop independently from display cleanup', () => {
    expect(formatStreamStop({
      clientId: 'z-fold-7',
      stream: {
        sessionId: 'z-fold-7-steam-shortcut:3767414131',
        clientId: 'z-fold-7',
        appId: 'steam-shortcut:3767414131',
        displayId: 'client-z-fold-7',
        codec: 'h264',
        fps: 120,
        initialBitrateMbps: 65,
        transport: 'lan-direct',
        state: 'stopped',
        error: null
      }
    })).toBe('stopped z-fold-7-steam-shortcut:3767414131');
  });

  it('reports fixed simulator telemetry facts without a policy draft', () => {
    const payload = createTelemetryPayload();

    expect(payload).toMatchObject({
      rttMs: 8,
      packetLossPercent: 0,
      decoderLoadPercent: 20,
      estimatedBandwidthMbps: 120,
      wifiBand: '6-ghz',
      batteryPercent: 80,
      thermalState: 'nominal'
    });
  });

  it('formats plan details with reason', () => {
    const plan: PlanResponse = {
      display: {
        mode: 'physical-blackout',
        width: 2560,
        height: 1600,
        refreshHz: 120,
        reason:
          'Display mode physical-blackout selected by server profile policy; physical display recovery remains available. HDR disabled because virtual display does not report HDR capability.'
      },
      stream: {
        codec: 'av1',
        fps: 120,
        initialBitrateMbps: 65,
        transport: 'lan-direct',
        congestionPolicy: 'adaptive',
        reason: 'Excellent LAN telemetry kept 120 FPS.'
      }
    };

    expect(formatPlanDetails(plan)).toContain('Excellent LAN');
    expect(formatPlanDetails(plan)).toContain('physical-blackout selected by server profile policy');
  });

  it('creates a deterministic pointer gesture payload for no-phone testing', () => {
    expect(createPointerGesturePayload(7)).toEqual({
      sequence: 7,
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
    });
  });

  it('creates a deterministic keyboard input payload for no-phone testing', () => {
    expect(createKeyboardInputPayload(8)).toEqual({
      sequence: 8,
      events: [
        {
          type: 'keyboard',
          action: 'press',
          key: 'Escape',
          code: 'Escape'
        }
      ]
    });
  });

  it('formats input acceptance summaries', () => {
    expect(formatInputAccepted({ accepted: true, eventCount: 3, sessionId: 'session-1' })).toBe('input accepted 3 event(s) session-1');
  });

  it('formats active and inactive beacon states', () => {
    expect(
      formatBeaconState({
        state: 'active',
        displayId: 'client-z-fold-7',
        leasePrepared: true,
        displayRemoved: false
      })
    ).toBe('beacon active client-z-fold-7 prepared');

    expect(
      formatBeaconState({
        state: 'inactive',
        displayId: 'client-z-fold-7',
        leasePrepared: false,
        displayRemoved: true
      })
    ).toBe('beacon inactive client-z-fold-7 removed');
  });

  it('keeps automatic fingerprints stable until the simulated network changes', () => {
    const first = createBenchmarkPreparePayload('automatic', 'home-wifi');
    const repeated = createBenchmarkPreparePayload('automatic', 'home-wifi');
    const changed = createBenchmarkPreparePayload('automatic', 'mobile-hotspot');

    expect(repeated).toEqual(first);
    expect(changed.fingerprints.network).not.toEqual(first.fingerprints.network);
    expect(changed.fingerprints.hardware).toEqual(first.fingerprints.hardware);
  });

  it('manual benchmark uses the same facts but always requests a manual run', () => {
    const automatic = createBenchmarkPreparePayload('automatic', 'home-wifi');
    const manual = createBenchmarkPreparePayload('manual', 'home-wifi');

    expect(manual.trigger).toBe('manual');
    expect(manual.fingerprints).toEqual(automatic.fingerprints);
  });

  it('creates complete raw evidence from the server-issued benchmark suite', () => {
    const prepared: BenchmarkPrepareResponse = {
      disposition: 'start-new',
      runId: '11111111-1111-1111-1111-111111111111',
      networkCoverage: { firstSequence: 0, expectedPacketCount: 3 },
      transportPlan: { datagramPayloadBytes: 1000 },
      hardwarePlan: {
        decoderRounds: [
          {
            codec: 'h264',
            profile: 'high',
            bitDepth: 8,
            width: 640,
            height: 360,
            targetFps: 30
          }
        ]
      }
    };

    const completion = createBenchmarkCompletionPayload(prepared);

    expect(completion.networkSamples).toHaveLength(3);
    expect(completion.networkSamples[2]).toMatchObject({
      sequence: 2,
      payloadBytes: 1000,
      received: true,
      throughputMbps: 100
    });
    expect(completion.decoderSamples).toEqual([
      expect.objectContaining({
        codec: 'h264',
        width: 640,
        height: 360,
        targetFps: 30,
        configured: true,
        sustainedFps: 30
      })
    ]);
    expect(completion.powerSamples).toHaveLength(2);
  });

  it('creates network-only session preflight evidence without inventing decoder results', () => {
    const prepared: BenchmarkPrepareResponse = {
      disposition: 'start-new',
      runId: '22222222-2222-2222-2222-222222222222',
      networkCoverage: { firstSequence: 4, expectedPacketCount: 2 },
      transportPlan: { datagramPayloadBytes: 1200 },
      hardwarePlan: { decoderRounds: [] }
    };

    const completion = createBenchmarkCompletionPayload(prepared);

    expect(completion.networkSamples).toHaveLength(2);
    expect(completion.decoderSamples).toEqual([]);
    expect(completion.powerSamples).toEqual([
      { batteryPercent: 100, isCharging: true, thermalState: 'nominal' }
    ]);
  });
});
