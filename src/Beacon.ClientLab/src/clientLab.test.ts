import { describe, expect, it } from 'vitest';
import {
  createDefaultProfile,
  createGamePlanRequest,
  createTelemetryPayload,
  formatLaunchEvents,
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
        error: null
      }
    };

    expect(formatLaunchEvents(launch)).toEqual([
      'streaming client-z-fold-7',
      'running av1 120fps'
    ]);
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
      display: { mode: 'virtual-primary', width: 2560, height: 1600, refreshHz: 120 },
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
  });
});
