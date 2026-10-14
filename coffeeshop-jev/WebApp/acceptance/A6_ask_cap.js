async (page) => {
  await fresh(page);
  await say(page, 'something sweet');
  await say(page, 'something sweet');
  await say(page, 'something sweet');
  ok((await lastReply(page)).toLowerCase().includes("couldn't understand"), 'ask cap message: ' + await lastReply(page));
  await page.getByTestId('nav-orders').click();
  await page.waitForSelector('[data-testid=history-row]');
  ok((await page.locator('[data-testid=history-row]').first().innerText()).includes('Not placed'), 'cap shows as Not placed');
  return 'A6 ok';
}
