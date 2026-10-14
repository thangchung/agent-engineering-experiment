// Prelude pasted at the top of every scenario by run.sh (playwright-cli run-code takes one self-contained function).
const URL = 'http://localhost:5200';
const ok = (cond, msg) => { if (!cond) throw new Error('ASSERT: ' + msg); };
const fresh = async (page) => { await page.goto(URL); await page.evaluate(() => localStorage.removeItem('cs.guest')); await page.goto(URL); await page.waitForSelector('.sc-ai-input__textarea:not([disabled])'); await page.waitForFunction(() => document.querySelector('[data-testid=guest-btn]')?.textContent.includes('guest-')); };
const assistantCount = (page) => page.locator('.sc-ai-message--assistant').count();
const say = async (page, text) => {
  const before = await assistantCount(page);
  await page.locator('.sc-ai-input__textarea').fill(text);
  await page.keyboard.press('Enter');
  await page.waitForFunction((n) => document.querySelectorAll('.sc-ai-message--assistant').length > n && !document.querySelector('.sc-ai-input__textarea').disabled, before, { timeout: 150000 });
};
const lastReply = (page) => page.locator('.sc-ai-message--assistant').last().innerText();
