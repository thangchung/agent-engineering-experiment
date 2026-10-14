async (page) => {
  await fresh(page);
  await say(page, "what's the weather today?");
  ok((await lastReply(page)).toLowerCase().includes('food and drink'), 'polite refusal: ' + await lastReply(page));
  ok(await page.locator('[data-testid=ticket-steps] li.fail').count() === 1, 'gate step failed on the ticket');
  await page.getByTestId('nav-orders').click();
  await page.waitForSelector('[data-testid=history-row]');
  ok((await page.locator('[data-testid=history-row]').first().innerText()).includes('Not placed'), 'shown as Not placed');
  await page.getByTestId('filter-Completed').click();
  await page.getByTestId('history-empty').waitFor();
  return 'A3 ok';
}
