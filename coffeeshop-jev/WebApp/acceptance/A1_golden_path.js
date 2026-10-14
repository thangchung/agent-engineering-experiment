async (page) => {
  await fresh(page);
  await say(page, '2 lattes and a croissant');
  ok(await page.locator('[data-testid=ticket-steps] li.done').count() === 5, 'all 5 ticket steps done');
  const ticket = await page.locator('[data-testid=order-ticket]').innerText();
  ok(ticket.includes('Order #'), 'ticket shows an order number');
  ok(ticket.includes('$12.25'), 'total is the catalog price: 2 x 4.50 + 3.25 = $12.25, got: ' + ticket);
  ok(ticket.includes('Latte') && ticket.includes('Croissant'), 'both lines on the ticket');
  ok((await lastReply(page)).length > 0, 'assistant replied in chat');
  await page.getByTestId('nav-orders').click();
  await page.waitForSelector('[data-testid=history-row]');
  ok((await page.locator('[data-testid=history-row]').first().innerText()).includes('Completed'), 'order in history');
  return 'A1 ok';
}
