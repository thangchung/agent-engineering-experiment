async (page) => {
  await fresh(page);
  await say(page, 'something sweet');
  ok((await page.locator('[data-testid=hint]').innerText()).includes('Answer in your next message'), 'hint after a question');
  ok((await page.locator('[data-testid=ticket-steps]').innerText()).includes('Asked you (1 of 2)'), 'ask counter on the ticket');
  ok(await page.getByTestId('suggestions').locator('button').count() === 6, 'suggestions stay visible after a turn');
  await say(page, 'a muffin please');
  const ticket = await page.locator('[data-testid=order-ticket]').innerText();
  ok(ticket.includes('Muffin') && ticket.includes('$3.00'), 'answer completes the order: ' + ticket);
  ok((await page.locator('[data-testid=hint]').innerText()) === '', 'hint cleared');
  return 'A2 ok';
}
