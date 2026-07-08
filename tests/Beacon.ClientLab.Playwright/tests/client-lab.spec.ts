import { expect, test } from '@playwright/test';

test('simulates hello, profile patch, beacon, plan, disconnect, reconnect, quit, and emergency restore', async ({ page }) => {
  const capabilityBodies: unknown[] = [];
  const telemetryBodies: unknown[] = [];
  const beaconBodies: unknown[] = [];
  const inputBodies: unknown[] = [];

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
            launch: { type: 'steam-rungameid', command: 'steam://rungameid/16180920483166814208' },
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
  await page.route('**/clients/z-fold-7/capabilities', async route => {
    capabilityBodies.push(route.request().postDataJSON());
    await route.fulfill({ contentType: 'application/json', body: JSON.stringify({ accepted: true }) });
  });
  await page.route('**/clients/z-fold-7/telemetry', async route => {
    telemetryBodies.push(route.request().postDataJSON());
    await route.fulfill({ contentType: 'application/json', body: JSON.stringify({ accepted: true }) });
  });
  await page.route('**/clients/z-fold-7/beacon', async route => {
    const body = route.request().postDataJSON();
    beaconBodies.push(body);
    await route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        state: body.active ? 'active' : 'inactive',
        displayId: 'client-z-fold-7',
        leasePrepared: body.active,
        displayRemoved: !body.active
      })
    });
  });
  await page.route('**/clients/z-fold-7/plan', async route => {
    expect(route.request().postDataJSON()).toEqual({ gameId: 'steam-shortcut:3767414131' });
    await route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        clientId: 'z-fold-7',
        appId: 'steam-shortcut:3767414131',
        display: {
          displayId: 'client-z-fold-7',
          width: 2560,
          height: 1600,
          refreshHz: 120,
          mode: 'virtual-primary',
          reason:
            'Display mode virtual-primary selected by server profile policy. HDR disabled because virtual display does not report HDR capability.'
        },
        stream: {
          fps: 120,
          codec: 'av1',
          initialBitrateMbps: 65,
          transport: 'lan-direct',
          congestionPolicy: 'adaptive',
          reason: 'Excellent LAN telemetry kept 120 FPS.'
        },
        recovery: { restorePhysicalDisplayOnEnd: true, allowClientAbort: true }
      })
    });
  });
  await page.route('**/clients/z-fold-7/launch', async route => {
    expect(route.request().postDataJSON()).toEqual({ gameId: 'steam-shortcut:3767414131' });
    await route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        clientId: 'z-fold-7',
        state: 'streaming',
        displayId: 'client-z-fold-7',
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
      })
    });
  });
  await page.route('**/clients/z-fold-7/input', async route => {
    const body = route.request().postDataJSON();
    inputBodies.push(body);
    await route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        accepted: true,
        eventCount: body.events.length,
        sessionId: 'z-fold-7-steam-shortcut:3767414131'
      })
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
    await route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({ clientId: 'z-fold-7', displayId: 'client-z-fold-7', recovered: true })
    });
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
  await page.getByRole('button', { name: 'Active Beacon', exact: true }).click();
  await expect(page.getByText('beacon active client-z-fold-7 prepared')).toBeVisible();
  expect(beaconBodies).toEqual([{ active: true }]);

  await page.getByRole('button', { name: 'Plan' }).click();
  await expect(page.getByText('virtual-primary')).toBeVisible();
  await expect(page.getByText('Display mode virtual-primary selected by server profile policy.')).toBeVisible();
  await expect(page.getByText('Excellent LAN telemetry kept 120 FPS.')).toBeVisible();
  expect(capabilityBodies).toHaveLength(1);
  expect(telemetryBodies).toHaveLength(1);
  expect(capabilityBodies[0]).toMatchObject({ maxFps: 120, currentScreenMode: '2560x1600@120' });
  expect(telemetryBodies[0]).toMatchObject({ rttMs: 8, wifiBand: 'wifi-7' });

  await page.getByRole('button', { name: 'Launch' }).click();
  await expect(page.getByText('streaming client-z-fold-7')).toBeVisible();
  await expect(page.getByText('running av1 120fps')).toBeVisible();
  await expect(page.getByText('beacon-fake://stream/z-fold-7-steam-shortcut:3767414131')).toBeVisible();
  expect(capabilityBodies).toHaveLength(2);
  expect(telemetryBodies).toHaveLength(2);

  await page.getByRole('button', { name: 'Send Pointer' }).click();
  await expect(page.getByText('input accepted 3 event(s) z-fold-7-steam-shortcut:3767414131')).toBeVisible();
  expect(inputBodies).toEqual([
    {
      sequence: 1,
      events: [
        { type: 'pointer', action: 'down', pointerId: 1, x: 0.5, y: 0.5, buttons: 1 },
        { type: 'pointer', action: 'move', pointerId: 1, x: 0.75, y: 0.25 },
        { type: 'pointer', action: 'up', pointerId: 1, x: 0.75, y: 0.25, buttons: 1 }
      ]
    }
  ]);

  await page.getByRole('button', { name: 'Send Keyboard' }).click();
  await expect(page.getByText('input accepted 1 event(s) z-fold-7-steam-shortcut:3767414131')).toBeVisible();
  expect(inputBodies).toEqual([
    {
      sequence: 1,
      events: [
        { type: 'pointer', action: 'down', pointerId: 1, x: 0.5, y: 0.5, buttons: 1 },
        { type: 'pointer', action: 'move', pointerId: 1, x: 0.75, y: 0.25 },
        { type: 'pointer', action: 'up', pointerId: 1, x: 0.75, y: 0.25, buttons: 1 }
      ]
    },
    {
      sequence: 2,
      events: [{ type: 'keyboard', action: 'press', key: 'Escape', code: 'Escape' }]
    }
  ]);

  await page.getByRole('button', { name: 'Disconnect' }).click();
  await expect(page.getByText('lease retained')).toBeVisible();

  await page.getByRole('button', { name: 'Reconnect' }).click();
  await expect(page.getByText('reconnected client-z-fold-7')).toBeVisible();

  await page.getByRole('button', { name: 'Quit' }).click();
  await expect(page.getByText('cleanup evaluated')).toBeVisible();

  await page.getByRole('button', { name: 'Inactive Beacon', exact: true }).click();
  await expect(page.getByText('beacon inactive client-z-fold-7 removed')).toBeVisible();
  expect(beaconBodies).toEqual([{ active: true }, { active: false }]);

  await page.getByRole('button', { name: 'Restore' }).click();
  await expect(page.getByText('recovered client-z-fold-7')).toBeVisible();
});
