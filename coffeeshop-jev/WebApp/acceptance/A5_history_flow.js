async (page) => {
  await fresh(page);
  await say(page, 'what did I order before?');
  ok((await lastReply(page)).includes("haven't ordered anything yet"), 'empty-history template for a new guest: ' + await lastReply(page));
  await say(page, 'a latte');
  await say(page, 'what did I order before?');
  ok(await page.locator('[data-testid=history-card] li').count() === 1, 'history card lists the 1 order');
  const oldId = await page.getByTestId('guest-btn').innerText();
  await page.getByTestId('guest-btn').click();
  await page.getByTestId('new-guest').click();
  await page.waitForFunction((id) => document.querySelector('[data-testid=guest-btn]').textContent.trim() !== id, oldId);
  await page.getByTestId('nav-orders').click();
  await page.waitForSelector('[data-testid=history-empty]');
  ok((await page.getByTestId('history-empty').innerText()).includes("You haven't ordered anything yet"), 'new guest has an empty history page');
  await page.getByTestId('scope-all').click();
  await page.waitForSelector('[data-testid=history-row]');
  ok(await page.locator('[data-testid=history-row]').count() >= 1, 'Everyone today still lists the earlier order');
  return 'A5 ok';
}
