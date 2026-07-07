import { describe, expect, it } from 'vitest';
import { createDefaultProfile, createGamePlanRequest, validateProfileDraft } from './clientLab';

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
});
