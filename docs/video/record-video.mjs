// Records the four Anamnesis demo scenes as .webm clips via Playwright.
// Prereq: API running at http://localhost:5177 with ingested corpus.
// Run from docs/video:  node record-video.mjs
import { chromium } from 'playwright';
import { fileURLToPath } from 'url';
import path from 'path';

const here = path.dirname(fileURLToPath(import.meta.url));
const out = path.join(here, 'clips');
const fileUrl = (p) => 'file:///' + p.replace(/\\/g, '/');
const APP = 'http://localhost:5177';

const browser = await chromium.launch();

async function record(name, fn) {
  const context = await browser.newContext({
    viewport: { width: 1280, height: 720 },
    recordVideo: { dir: out, size: { width: 1280, height: 720 } }
  });
  const page = await context.newPage();
  await fn(page);
  const video = page.video();
  await context.close();
  await video.saveAs(path.join(out, `${name}.webm`));
  console.log(`recorded ${name}`);
}

function addCaption(page, text) {
  return page.evaluate((t) => {
    let cap = document.getElementById('vidcap');
    if (!cap) {
      cap = document.createElement('div');
      cap.id = 'vidcap';
      cap.style.cssText = 'position:fixed;left:0;right:0;bottom:0;height:54px;background:rgba(31,35,40,.93);' +
        'display:flex;align-items:center;justify-content:center;font-family:Segoe UI,sans-serif;' +
        'font-size:20px;color:#fafaf7;z-index:9999;text-align:center;';
      document.body.appendChild(cap);
    }
    cap.textContent = t;
  }, text);
}

async function ask(page, question) {
  await page.fill('#question', '');
  await page.type('#question', question, { delay: 45 });
  await page.waitForTimeout(400);
  await page.click('#ask');
  await page.waitForFunction(() => document.getElementById('answer').textContent.length > 0, null, { timeout: 90000 });
}

// 1 — intro: the landing page, idle
await record('1-intro', async (page) => {
  await page.goto(APP, { waitUntil: 'networkidle' });
  await page.waitForTimeout(6000);
});

// 2 — ask a covered question; read; show citations + status line
await record('2-ask', async (page) => {
  await page.goto(APP, { waitUntil: 'networkidle' });
  await page.waitForTimeout(1000);
  await ask(page, 'What is the audit problem?');
  await page.waitForTimeout(6500);
  await page.evaluate(() => document.getElementById('citations').scrollIntoView({ behavior: 'smooth', block: 'center' }));
  await addCaption(page, 'Inline [n] citations map to the exact source post — similarity scores included');
  await page.waitForTimeout(5500);
  await addCaption(page, 'Provider and latency on every answer — Claude primary, automatic OpenAI failover (Polly)');
  await page.evaluate(() => document.getElementById('status').scrollIntoView({ behavior: 'smooth', block: 'center' }));
  await page.waitForTimeout(5000);
});

// 3 — the miss: an uncovered question declines instead of inventing
await record('3-declines', async (page) => {
  await page.goto(APP, { waitUntil: 'networkidle' });
  await page.waitForTimeout(800);
  await addCaption(page, 'The grounding guard: what happens when the sources do NOT cover the question?');
  await ask(page, 'What did I write about cricket?');
  await page.waitForTimeout(1500);
  await addCaption(page, 'It declines rather than inventing — faithfulness held at 1.00 on the eval baseline');
  await page.waitForTimeout(6500);
});

// 4 — outro card
await record('4-outro', async (page) => {
  await page.goto(fileUrl(path.join(here, 'outro.html')));
  await page.waitForTimeout(9000);
});

await browser.close();
console.log('all clips recorded');
