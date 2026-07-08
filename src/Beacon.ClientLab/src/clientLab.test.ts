import { describe, expect, it } from 'vitest';
import {
  createDefaultProfile,
  createGamePlanRequest,
  createKeyboardInputPayload,
  createPointerGesturePayload,
  createTelemetryPayload,
  formatLaunchEvents,
  formatInputAccepted,
  formatPlanDetails,
  validateProfileDraft,
  type LaunchResponse,
  type PlanResponse
} from './clientLab';

describe('Client Lab profile validation', () => {
  it('blocks the Z Fold 7 2560x1440 collapse before a profile patch', () => {
    const profile = createDefaultProfile();

    const result = validateProfileDraft({ ...profile, preferredHeight: 1440 });

    expect(result.ok).toBe(false);
    expect(result.message).toContain('2560x1440');
  });

  it('accepts the Z Fold 7 2560x1600 default', () => {
    const result = validateProfileDraft(createDefaultProfile());

    expect(result.ok).toBe(true);
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
        error: null,
        connection: {
          protocol: 'beacon-fake',
          launchUri: 'beacon-fake://stream/z-fold-7-steam-shortcut:3767414131',
          endpoints: [{ role: 'control', uri: 'beacon-fake://stream/z-fold-7-steam-shortcut:3767414131' }],
          metadata: { displayId: 'client-z-fold-7' }
        }
      }
    };

    expect(formatLaunchEvents(launch)).toEqual([
      'streaming client-z-fold-7',
      'running av1 120fps',
      'beacon-fake://stream/z-fold-7-steam-shortcut:3767414131'
    ]);
    expect(launch.stream?.connection?.protocol).toBe('beacon-fake');
    expect(launch.stream?.connection?.launchUri).toBe('beacon-fake://stream/z-fold-7-steam-shortcut:3767414131');
  });

  it('builds telemetry payloads from named profiles', () => {
    const payload = createTelemetryPayload('thermal-battery');

    expect(payload).toMatchObject({
      rttMs: 12,
      packetLossPercent: 0,
      decoderLoadPercent: 88,
      estimatedBandwidthMbps: 100,
      wifiBand: 'wifi-6',
      batteryPercent: 9,
      thermalState: 'hot'
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
});
