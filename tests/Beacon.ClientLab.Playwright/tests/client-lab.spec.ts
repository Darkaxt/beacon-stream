import { expect, test } from '@playwright/test';

test('simulates hello, profile patch, plan, disconnect, reconnect, quit, and emergency restore', async ({ page }) => {
  await page.route('**/clients/hello', async route => {
    await route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        clientId: 'z-fold-7',
        profile: {
          display: {
            preferredWidth: 2560,
            preferredHeight: 1600,
            preferredRefreshHz: 120,
            hdrPreference: 'Prefer'
          },
          stream: {
            codecPreference: 'auto',
            qualityMode: 'auto',
            bitrateCapMbps: null
          }
        },
        editableFields: ['preferredWidth', 'preferredHeight', 'preferredRefreshHz']
      })
    });
  });
  await page.route('**/games', async route => {
    await route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        games: [
          {
            id: 'steam-shortcut:3767414131',
            title: 'Dispatch',
            source: 'steam-shortcut',
            launch: { type: 'steam-rungameid', command: 'steam://rungameid/16180979725241544704' },
            artwork: { coverPath: null, source: 'none' },
            installed: true
          }
        ],
        diagnostics: []
      })
    });
  });
  await page.route('**/clients/z-fold-7/profile', async route => {
    if (route.request().method() === 'PATCH') {
      const body = route.request().postDataJSON();
      if (body.preferredWidth === 2560 && body.preferredHeight === 1440) {
        await route.fulfill({ status: 400, contentType: 'application/json', body: JSON.stringify({ error: '2560x1440 blocked' }) });
        return;
      }
      await route.fulfill({ contentType: 'application/json', body: JSON.stringify({ ok: true }) });
      return;
    }

    await route.fulfill({ contentType: 'application/json', body: JSON.stringify({}) });
  });
  await page.route('**/clients/z-fold-7/plan', async route => {
    expect(route.request().postDataJSON()).toEqual({ gameId: 'steam-shortcut:3767414131' });
    await route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        clientId: 'z-fold-7',
        appId: 'steam-shortcut:3767414131',
        display: { displayId: 'client-z-fold-7', width: 2560, height: 1600, refreshHz: 120, mode: 'virtual-primary' },
        stream: { fps: 120, codec: 'av1', initialBitrateMbps: 65 },
        recovery: { restorePhysicalDisplayOnEnd: true, allowClientAbort: true }
      })
    });
  });
  await page.route('**/clients/z-fold-7/launch', async route => {
    expect(route.request().postDataJSON()).toEqual({ gameId: 'steam-shortcut:3767414131' });
    await route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({ state: 'started', displayId: 'client-z-fold-7' })
    });
  });
  await page.route('**/clients/z-fold-7/disconnect', async route => {
    await route.fulfill({ contentType: 'application/json', body: JSON.stringify({ leaseRetained: true }) });
  });
  await page.route('**/clients/z-fold-7/reconnect', async route => {
    await route.fulfill({ contentType: 'application/json', body: JSON.stringify({ state: 'reconnected', displayId: 'client-z-fold-7' }) });
  });
  await page.route('**/clients/z-fold-7/quit', async route => {
    await route.fulfill({ contentType: 'application/json', body: JSON.stringify({ cleanupEvaluated: true, displayRemoved: true }) });
  });
  await page.route('**/clients/z-fold-7/emergency-restore', async route => {
    await route.fulfill({ contentType: 'application/json', body: JSON.stringify({ restoreRequested: true }) });
  });

  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Beacon Client Lab' })).toBeVisible();
  await page.getByRole('button', { name: 'Hello' }).click();
  await expect(page.getByText('hello z-fold-7')).toBeVisible();
  await expect(page.getByLabel('Width')).toHaveValue('2560');
  await expect(page.getByLabel('Height')).toHaveValue('1600');
  await expect(page.getByLabel('Refresh')).toHaveValue('120');
  await expect(page.getByLabel('Game')).toHaveValue('steam-shortcut:3767414131');
  await expect(page.getByText('steam-shortcut | installed | steam-rungameid')).toBeVisible();

  await page.getByLabel('Height').fill('1440');
  await page.getByRole('button', { name: 'Save Profile' }).click();
  await expect(page.getByText('2560x1440')).toBeVisible();

  await page.getByLabel('Height').fill('1600');
  await page.getByRole('button', { name: 'Plan' }).click();
  await expect(page.getByText('virtual-primary')).toBeVisible();

  await page.getByRole('button', { name: 'Launch' }).click();
  await expect(page.getByText('started client-z-fold-7')).toBeVisible();

  await page.getByRole('button', { name: 'Disconnect' }).click();
  await expect(page.getByText('lease retained')).toBeVisible();

  await page.getByRole('button', { name: 'Reconnect' }).click();
  await expect(page.getByText('reconnected client-z-fold-7')).toBeVisible();

  await page.getByRole('button', { name: 'Quit' }).click();
  await expect(page.getByText('cleanup evaluated')).toBeVisible();

  await page.getByRole('button', { name: 'Restore' }).click();
  await expect(page.getByText('restore requested')).toBeVisible();
});
