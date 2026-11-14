// Browser verification for face enrollment / demo face sign-in (Chromium fake webcam + loopback Face++ mock).
import { createRequire } from 'node:module';
import { mkdir } from 'node:fs/promises';
import { readdirSync } from 'node:fs';
const require = createRequire(import.meta.url);
const { chromium } = require('playwright');
const base = 'http://localhost:5199', evidence = process.argv[2], faces = process.argv[3];
const admin = { email: 'admin@example.test', password: 'Browser-Admin!2046' };
await mkdir(evidence, { recursive: true });
const results = [];
const check = async (id, fn) => {
  try { const detail = await fn(); results.push({ id, status: 'PASS', ...(detail ? { detail } : {}) }); }
  catch (e) { results.push({ id, status: 'FAIL', reason: e.message.split('\n')[0] }); }
};
const assert = (ok, msg) => { if (!ok) throw new Error(msg); };
const mock = q => fetch('http://127.0.0.1:18990/control?' + q).then(r => r.json());
const trackCamera = () => {
  window.__tracks = [];
  const original = navigator.mediaDevices?.getUserMedia?.bind(navigator.mediaDevices);
  if (original) navigator.mediaDevices.getUserMedia = async c => { const s = await original(c); window.__tracks.push(...s.getTracks()); return s; };
};
const cameraOff = page => page.evaluate(() => window.__tracks.length > 0 && window.__tracks.every(t => t.readyState === 'ended'));

const browser = await chromium.launch({ args: ['--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream'] });
const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 });
await context.addInitScript(trackCamera);
const page = await context.newPage();
const errors = [];
page.on('pageerror', e => errors.push(e.message));

async function passwordLogin() {
  await page.goto(base + '/Account/Login');
  await page.fill('#Email', admin.email); await page.fill('#Password', admin.password);
  await Promise.all([page.waitForURL(u => !u.pathname.startsWith('/Account/Login')), page.click('#password-sign-in button[type=submit]')]);
}
async function logout() {
  await page.locator('form[action*="Logout"] button').first().click();
  await page.waitForURL(/\/Account\/Login/);
}
async function captureSteps(scope) {
  for (let step = 1; step <= 3; step++) {
    await page.locator(scope + ' [data-face-capture]').waitFor({ state: 'visible' });
    await page.click(scope + ' [data-face-capture]');
    if (step < 3) await page.waitForFunction(([s, n]) => document.querySelector(s + ' [data-face-status]').textContent.startsWith('Step ' + n), [scope, step + 1]);
  }
}
async function overflow(path, name, prepare) {
  const out = [];
  for (const width of [1440, 1024, 768, 390, 320]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(base + path);
    if (prepare) await prepare();
    const wide = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    const stage = await page.locator('[data-face-stage]').first().boundingBox().catch(() => null);
    assert(wide <= 0, `${name} overflows by ${wide}px at ${width}px`);
    assert(!stage || stage.width > 200 && stage.x >= 0 && stage.x + stage.width <= width, `${name} camera not fully visible at ${width}px`);
    out.push(width + ':ok');
  }
  await page.setViewportSize({ width: 1440, height: 900 });
  return out.join(' ');
}
const shot = async (name, width = 1440) => {
  const p = await context.browser().newPage({ viewport: { width, height: 900 }, deviceScaleFactor: 2, storageState: await context.storageState() });
  await p.goto(page.url()); await p.waitForLoadState('networkidle');
  await p.screenshot({ path: `${evidence}/${name}.png`, fullPage: true }); await p.close();
};

await check('F01-login-renders-three-methods', async () => {
  await page.goto(base + '/Account/Login');
  const tabs = await page.locator('[role=tab]').allInnerTexts();
  assert(tabs.map(t => t.trim()).join('|') === 'Password|Face|Passkey', 'tabs: ' + tabs);
  assert(await page.locator('.workspace-bar, .sidebar').count() === 0, 'dashboard navigation shown before sign-in');
  await page.screenshot({ path: `${evidence}/login-password-1440.png` });
});
await check('F02-face-tab-opens-keyboard', async () => {
  await page.focus('#tab-password'); await page.keyboard.press('ArrowRight');
  assert(await page.locator('#panel-face').isVisible(), 'face panel hidden');
  assert(await page.evaluate(() => document.activeElement.id) === 'tab-face', 'focus did not move to the Face tab');
  assert(await page.locator('#panel-password').isHidden(), 'password panel still visible');
  assert(await page.locator('label[for=face-email]').count() === 1, 'face email label missing');
  await page.screenshot({ path: `${evidence}/login-face-1440.png` });
});
await check('F03-login-responsive', () => overflow('/Account/Login', 'Login', () => page.click('#tab-face')));
await check('F04-password-login-to-role-dashboard', async () => { await passwordLogin(); assert(page.url().endsWith('/AdminDashboard'), page.url()); });
await check('F05-setup-uses-role-workspace-layout', async () => {
  await page.goto(base + '/Profile?tab=security');
  assert(await page.locator('aside.sidebar').count() === 1, 'role sidebar missing');
  assert(await page.locator('.workspace-bar .bar-profile').count() === 1, 'workspace header missing');
  assert(await page.locator('.portal-bar').count() === 0, 'public portal header still rendered');
  assert(await page.locator('.account-tabs a[aria-current=page]').innerText() === 'Security', 'heading');
  assert((await page.locator('#face-setup-title').innerText()) === 'Face Recognition Setup', 'face heading');
  assert((await page.locator('[data-face-badge]').innerText()).includes('Not enrolled'), 'state badge');
  await shot('setup-not-enrolled-1440');
});
await check('F06-setup-responsive', () => overflow('/Profile?tab=security', 'Setup'));
await check('F07-enroll-requires-password', async () => {
  await page.goto(base + '/Profile?tab=security');
  await page.click('[data-face-start]');
  assert((await page.locator('[data-face-status]').innerText()).includes('current password'), 'no password prompt');
  assert(await page.evaluate(() => window.__tracks.length) === 0, 'camera opened without password');
});
await check('F08-enroll-camera-capture-save', async () => {
  await mock('reject=0');
  await page.fill('[data-face-password]', admin.password);
  await page.click('[data-face-start]');
  await page.waitForFunction(() => document.querySelector('[data-face-status]').textContent.startsWith('Step 1'));
  assert(await page.evaluate(() => document.querySelector('[data-face-video]').videoWidth > 0), 'no live video');
  await page.screenshot({ path: `${evidence}/setup-camera-step1-1440.png` });
  await captureSteps('[data-face-lab]');
  await page.locator('[data-face-save]').waitFor({ state: 'visible' });
  assert(await cameraOff(page), 'camera still active while waiting for save');
  await page.click('[data-face-save]');
  await page.waitForFunction(() => document.querySelector('[data-face-lab]').dataset.state === 'success');
  const files = readdirSync(faces).filter(f => f.endsWith('.face'));
  assert(files.length === 1, 'reference files: ' + files.length);
  await page.waitForLoadState('load'); await page.waitForTimeout(2200);
  assert((await page.locator('[data-face-badge]').innerText()).includes('Enrolled'), 'badge not Enrolled after reload');
  assert((await page.locator('.notice').first().innerText()).includes('Face recognition is set up'), 'success notice missing');
  await shot('setup-enrolled-1440');
  return 'encrypted reference file stored outside wwwroot';
});
await check('F09-ajax-navigation-initializes-and-releases-camera', async () => {
  await page.goto(base + '/Profile');
  await page.click('.account-tabs a[href*="security"]');
  await page.locator('[data-face-lab][data-ready]').waitFor();
  const same = await page.evaluate(() => performance.getEntriesByType('navigation').length);
  await page.fill('[data-face-password]', admin.password);
  await page.click('[data-face-start]');
  await page.waitForFunction(() => document.querySelector('[data-face-status]').textContent.startsWith('Step 1'));
  await page.click('aside.sidebar a[href="/AdminDashboard"], aside.sidebar a[href*="AdminDashboard"]');
  await page.waitForURL(/AdminDashboard/);
  await page.waitForFunction(() => window.__tracks.length > 0 && window.__tracks.every(t => t.readyState === 'ended'), null, { timeout: 5000 });
  return 'navigation entries=' + same;
});
await check('F10-face-login-success-creates-session', async () => {
  await logout();
  await page.click('#tab-face');
  await page.fill('[data-face-email]', admin.email);
  await page.click('#panel-face [data-face-start]');
  await page.waitForFunction(() => document.querySelector('#panel-face [data-face-status]').textContent.startsWith('Step 1'));
  await page.screenshot({ path: `${evidence}/login-face-camera-1440.png` });
  await captureSteps('#panel-face');
  await page.waitForURL(/AdminDashboard/, { timeout: 10000 });
  assert(await page.locator('.workspace-bar .bar-profile small').textContent().then(t => t.includes(admin.email)), 'not signed in as admin');
  const cookies = await context.cookies();
  assert(cookies.some(c => c.name === '.STMS.BiometricAuth.v2'), 'no Identity cookie');
});
await check('F11-face-login-mismatch-stays-unauthenticated', async () => {
  await logout();
  await mock('reject=1');
  await page.click('#tab-face');
  await page.fill('[data-face-email]', admin.email);
  await page.click('#panel-face [data-face-start]');
  await page.waitForFunction(() => document.querySelector('#panel-face [data-face-status]').textContent.startsWith('Step 1'));
  await captureSteps('#panel-face');
  await page.waitForFunction(() => document.querySelector('#panel-face [data-face-lab]').dataset.state === 'failed');
  const message = await page.locator('#panel-face [data-face-status]').innerText();
  assert(message.includes('Face verification failed'), message);
  assert(await cameraOff(page), 'camera still active after failure');
  await page.screenshot({ path: `${evidence}/login-face-failed-1440.png` });
  const status = await page.evaluate(() => fetch('/AdminDashboard', { redirect: 'manual' }).then(r => r.type === 'opaqueredirect' ? 302 : r.status));
  assert(status === 302, 'dashboard reachable: ' + status);
  assert(!(await context.cookies()).some(c => c.name === '.STMS.BiometricAuth.v2'), 'Identity cookie issued after failure');
  await mock('reject=0');
});
await check('F12-cancel-releases-camera', async () => {
  await page.goto(base + '/Account/Login');
  await page.click('#tab-face'); await page.fill('[data-face-email]', admin.email);
  await page.click('#panel-face [data-face-start]');
  await page.waitForFunction(() => document.querySelector('#panel-face [data-face-status]').textContent.startsWith('Step 1'));
  await page.click('#panel-face [data-face-cancel]');
  assert(await cameraOff(page), 'camera active after cancel');
  await page.click('#panel-face [data-face-start]');
  await page.waitForFunction(() => document.querySelector('#panel-face [data-face-status]').textContent.startsWith('Step 1'));
  await page.click('#tab-password');
  await page.waitForFunction(() => window.__tracks.every(t => t.readyState === 'ended'));
  return 'cancel and tab switch both stop all tracks';
});
await browser.close();

// Camera permission denied: no fake-UI auto-accept and no granted permission.
const denied = await chromium.launch({ args: ['--use-fake-device-for-media-stream', '--deny-permission-prompts'] });
await check('F13-camera-permission-denied', async () => {
  const p = await (await denied.newContext()).newPage();
  await p.goto(base + '/Account/Login');
  const cdp = await p.context().newCDPSession(p);
  await cdp.send('Browser.setPermission', { permission: { name: 'camera' }, setting: 'denied', origin: base }); await p.click('#tab-face'); await p.fill('[data-face-email]', admin.email);
  await p.click('#panel-face [data-face-start]');
  await p.waitForFunction(() => document.querySelector('#panel-face [data-face-lab]').dataset.state === 'failed', null, { timeout: 15000 });
  const message = await p.locator('#panel-face [data-face-status]').innerText();
  assert(/denied|permission/i.test(message), message);
  return message;
});
await denied.close();
const calls = (await mock('reject=0')).calls;
console.log(JSON.stringify({ results, errors, providerCalls: calls.length }, null, 1));
