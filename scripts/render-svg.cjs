const { chromium } = require('playwright');
const fs = require('fs');
(async () => {
  const [svgPath, outPath] = process.argv.slice(2);
  const svg = fs.readFileSync(svgPath, 'utf8');
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 960, height: 960 }, deviceScaleFactor: 1 });
  await page.setContent(`<html><body style="margin:0;background:transparent"><div style="width:960px;height:960px;display:flex;align-items:center;justify-content:center">${svg.replace('<svg ', '<svg width="940" height="940" ')}</div></body></html>`);
  await page.waitForTimeout(300);
  await page.screenshot({ path: outPath, omitBackground: true });
  await browser.close();
})();
