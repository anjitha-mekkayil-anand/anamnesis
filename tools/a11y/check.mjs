// Accessibility gate: axe-core against the bundled UI.
//
//   A11Y_URL=http://127.0.0.1:5099/ node check.mjs
//
// Fails on any violation at impact >= the threshold (default: serious).
// A gate that reports and passes is not a gate.

import { chromium } from 'playwright';
import { AxeBuilder } from '@axe-core/playwright';

const URL = process.env.A11Y_URL ?? 'http://127.0.0.1:5099/';
const THRESHOLD = process.env.A11Y_THRESHOLD ?? 'serious';

// axe impact levels, least to most severe.
const ORDER = ['minor', 'moderate', 'serious', 'critical'];
const minIndex = ORDER.indexOf(THRESHOLD);
if (minIndex === -1) {
  console.error(`Unknown A11Y_THRESHOLD "${THRESHOLD}" (use one of ${ORDER.join(', ')})`);
  process.exit(2);
}

const browser = await chromium.launch();
let failed = false;

try {
  // @axe-core/playwright requires a page from an explicit context —
  // browser.newPage() throws "Please use browser.newContext()".
  const context = await browser.newContext();
  const page = await context.newPage();
  const response = await page.goto(URL, { waitUntil: 'domcontentloaded', timeout: 30_000 });

  if (!response || !response.ok()) {
    console.error(`Could not load ${URL} (status ${response ? response.status() : 'none'})`);
    process.exit(2);
  }

  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze();

  const blocking = results.violations.filter(
    (v) => ORDER.indexOf(v.impact ?? 'minor') >= minIndex,
  );
  const advisory = results.violations.filter(
    (v) => ORDER.indexOf(v.impact ?? 'minor') < minIndex,
  );

  console.log(`axe on ${URL}`);
  console.log(`  passes:     ${results.passes.length}`);
  console.log(`  violations: ${results.violations.length} (${blocking.length} at >= ${THRESHOLD})`);

  const describe = (v) => {
    console.log(`\n  [${v.impact}] ${v.id} — ${v.help}`);
    console.log(`    ${v.helpUrl}`);
    for (const node of v.nodes.slice(0, 5)) {
      console.log(`    at: ${node.target.join(' ')}`);
    }
    if (v.nodes.length > 5) {
      console.log(`    ...and ${v.nodes.length - 5} more node(s)`);
    }
  };

  if (advisory.length) {
    console.log(`\nBelow threshold (not blocking):`);
    advisory.forEach(describe);
  }

  if (blocking.length) {
    console.log(`\nFAIL — ${blocking.length} violation(s) at impact >= ${THRESHOLD}:`);
    blocking.forEach(describe);
    failed = true;
  } else {
    console.log(`\nPASS — no violations at impact >= ${THRESHOLD}`);
  }
} finally {
  await browser.close();
}

process.exit(failed ? 1 : 0);
