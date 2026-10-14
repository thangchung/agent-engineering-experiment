async (page) => {
  await fresh(page);
  await say(page, 'what do you have?');
  ok(await page.locator('[data-testid=menu-card] li').count() === 11, 'inline menu card with 11 items');
  await page.getByTestId('nav-menu').click();
  await page.waitForSelector('[data-testid=menu-board]');
  ok(await page.locator('[data-testid=cartbar]').count() === 0, 'no cart bar when empty');
  await page.getByTestId('plus-LATTE').click();
  await page.getByTestId('plus-LATTE').click();
  await page.getByTestId('plus-MUFFIN').click();
  await page.getByTestId('cartbar').getByText('3 items in your order, $12.00').waitFor();
  await page.getByTestId('cart-count').filter({ hasText: /^3$/ }).waitFor();
  await page.getByTestId('minus-MUFFIN').click();
  await page.getByTestId('qty-MUFFIN').filter({ hasText: /^0$/ }).waitFor();
  await page.getByTestId('plus-MUFFIN').click();
  await page.getByTestId('qty-MUFFIN').filter({ hasText: /^1$/ }).waitFor();
  await page.getByTestId('cart-place').click();
  await page.waitForFunction(() => location.pathname === '/');
  await page.getByTestId('order-ticket').getByText('$12.00').waitFor({ timeout: 150000 });
  const chat = await page.locator('.sc-ai-message-list').innerText();
  ok(chat.includes('2 × Latte, 1 × Muffin'), 'cart sent as a chat message: ' + chat);
  const ticket = await page.getByTestId('order-ticket').innerText();
  ok(ticket.includes('$12.00'), 'ticket total $12.00: ' + ticket);
  await page.getByTestId('cart-count').waitFor({ state: 'detached' });
  return 'A4 ok';
}
