async (page) => {
  await fresh(page);
  await page.getByTestId('theme-toggle').click();
  await page.waitForFunction(() => document.documentElement.dataset.theme);
  const t = await page.evaluate(() => document.documentElement.dataset.theme);
  ok(t === 'dark' || t === 'light', 'theme pinned');
  await page.reload();
  ok(await page.evaluate(() => document.documentElement.dataset.theme) === t, 'theme survives reload');
  const box = page.locator('.sc-ai-input__textarea');
  await box.fill('line one');
  await page.keyboard.press('Shift+Enter');
  await page.keyboard.type('line two');
  ok((await box.inputValue()).includes('\n'), 'Shift+Enter inserts a newline');
  ok(await page.locator('.sc-ai-message--user').count() === 0, 'Shift+Enter does not send');
  await box.fill('x'.repeat(600));
  ok((await box.inputValue()).length === 500, 'input capped at 500 characters');
  await box.fill('');
  await page.getByTestId('guest-btn').click();
  await page.getByTestId('show-events').check();
  await say(page, 'what do you have?');
  await page.getByTestId('events').getByText('RUN_FINISHED').waitFor();
  const events = await page.getByTestId('events').innerText();
  ok(events.includes('RUN_STARTED') && events.includes('STATE_SNAPSHOT') && events.includes('RUN_FINISHED'), 'AG-UI event log: ' + events.slice(0, 200));
  await page.keyboard.press('Escape');
  await page.setViewportSize({ width: 390, height: 844 });
  for (const route of ['/', '/menu', '/orders']) {
    await page.goto(URL + route);
    await page.waitForSelector('main');
    ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'no horizontal scroll at 390px on ' + route);
  }
  await page.setViewportSize({ width: 1280, height: 720 });
  return 'A7 ok';
}
