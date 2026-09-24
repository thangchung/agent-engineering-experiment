// CoffeeShop.Jev counter UI. Plain fetch + ReadableStream SSE parsing, no framework
// (research.md §6: "simple HTML, no need for a WebHost").
"use strict";

const input = document.getElementById("order-input");
const submitBtn = document.getElementById("order-submit");
const askPanel = document.getElementById("ask-panel");
const askQuestion = document.getElementById("ask-question");
const askInput = document.getElementById("ask-input");
const askSubmit = document.getElementById("ask-submit");
const errorBanner = document.getElementById("error-banner");
const menuPanel = document.getElementById("menu-panel");
const menuItems = document.getElementById("menu-items");
const resultPanel = document.getElementById("result-panel");
const orderStatus = document.getElementById("order-status");
const orderMessage = document.getElementById("order-message");
const orderTotal = document.getElementById("order-total");
const colBarista = document.getElementById("col-barista");
const colKitchen = document.getElementById("col-kitchen");
const timeline = document.getElementById("timeline");
const board = document.getElementById("board");

let currentRunId = null;

function addTimelineEntry(kind, text) {
  const li = document.createElement("li");
  li.dataset.testid = `event-${kind}`;
  li.textContent = text;
  timeline.appendChild(li);
}

function showError(message) {
  errorBanner.textContent = message;
  errorBanner.hidden = false;
}

function resetForNewOrder() {
  errorBanner.hidden = true;
  resultPanel.hidden = true;
  menuPanel.hidden = true;
  menuItems.innerHTML = "";
  askPanel.hidden = true;
  colBarista.innerHTML = "";
  colKitchen.innerHTML = "";
  timeline.innerHTML = "";
}

async function refreshBoard() {
  try {
    const res = await fetch("/orders");
    if (!res.ok) return;
    const orders = await res.json();
    board.innerHTML = "";
    for (const order of orders) {
      const li = document.createElement("li");
      li.dataset.testid = "board-row";
      li.textContent = `#${order.number} — ${order.result.status} — $${order.result.total ?? 0}`;
      board.appendChild(li);
    }
  } catch {
    // The board is a nice-to-have; a failed refresh should not break the order flow.
  }
}

function renderTickets(result) {
  for (const ticket of result.tickets ?? []) {
    const list = ticket.station === "Barista" ? colBarista : colKitchen;
    for (const item of ticket.items ?? []) {
      const li = document.createElement("li");
      const label = item.minutes != null ? `${item.qty}x ${item.name} (~${item.minutes} min)` : `${item.qty}x ${item.name}`;
      li.appendChild(document.createTextNode(label));

      if (item.steps?.length) {
        const steps = document.createElement("ol");
        steps.className = "steps";
        for (const step of item.steps) {
          const stepLi = document.createElement("li");
          stepLi.textContent = step;
          steps.appendChild(stepLi);
        }
        li.appendChild(steps);
      }

      list.appendChild(li);
    }
  }
}

function renderDone(result) {
  resultPanel.hidden = false;
  orderStatus.textContent = result.status;
  orderMessage.textContent = result.message ?? "";
  orderTotal.textContent = result.total != null ? `Total: $${result.total}` : "";
  renderTickets(result);
  refreshBoard();
}

async function submitOrder() {
  const text = input.value.trim();
  if (!text) {
    showError("Please enter an order.");
    return;
  }

  resetForNewOrder();
  submitBtn.disabled = true;

  try {
    const response = await fetch("/orders", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ text }),
    });

    if (!response.ok) {
      showError("Could not place the order. Please try again.");
      return;
    }

    await readEventStream(response.body);
  } catch {
    showError("Something went wrong. Please try again.");
  } finally {
    submitBtn.disabled = false;
  }
}

async function readEventStream(body) {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  while (true) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });

    let boundary;
    while ((boundary = buffer.indexOf("\n\n")) >= 0) {
      const rawEvent = buffer.slice(0, boundary);
      buffer = buffer.slice(boundary + 2);
      handleSseChunk(rawEvent);
    }
  }
}

function handleSseChunk(rawEvent) {
  let eventType = "message";
  let data = "";
  for (const line of rawEvent.split("\n")) {
    if (line.startsWith("event:")) {
      eventType = line.slice(6).trim();
    } else if (line.startsWith("data:")) {
      data += line.slice(5).trim();
    }
  }

  let payload = null;
  try {
    payload = data ? JSON.parse(data) : null;
  } catch {
    payload = null;
  }

  switch (eventType) {
    case "run":
      currentRunId = payload?.runId ?? null;
      break;
    case "gate":
      addTimelineEntry("gate", `Gate: ${payload?.decisionType ?? "?"} (intent=${payload?.intentChoice ?? "?"})`);
      break;
    case "split":
      addTimelineEntry("split", "Order split between barista and kitchen");
      break;
    case "ask":
      addTimelineEntry("ask", "Asked for clarification");
      askQuestion.textContent = payload?.question ?? "Could you clarify your order?";
      askPanel.hidden = false;
      break;
    case "done":
      addTimelineEntry("done", `Done: ${payload?.status ?? "?"}`);
      askPanel.hidden = true;
      askInput.value = "";
      currentRunId = null;
      renderDone(payload ?? {});
      break;
    case "menu":
      // Showing the menu ends the run (ShowMenuExecutor yields output) - there is no longer
      // anything to answer, so the ask box (if it was ever shown) must not linger.
      askPanel.hidden = true;
      askInput.value = "";
      currentRunId = null;
      menuPanel.hidden = false;
      for (const item of payload ?? []) {
        const li = document.createElement("li");
        li.textContent = `${item.displayName} — $${item.priceUsd.toFixed(2)}`;
        menuItems.appendChild(li);
      }
      break;
    case "error":
      addTimelineEntry("error", payload?.message ?? "error");
      showError(payload?.message ?? "Something went wrong.");
      break;
    default:
      break;
  }
}

async function submitAnswer() {
  const text = askInput.value.trim();
  if (!text || !currentRunId) {
    return;
  }

  askSubmit.disabled = true;
  try {
    const response = await fetch(`/orders/${currentRunId}/answer`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ text }),
    });

    if (response.status === 404 || response.status === 409) {
      // The order this answer belonged to is gone (the app restarted, or it already finished/
      // timed out) - it can never accept an answer again. Don't leave the UI stuck waiting on
      // a dead order: tell the customer plainly and let them start over.
      addTimelineEntry("error", "This order is no longer waiting for an answer.");
      showError("This order can't take that answer anymore (it may have expired) - please place a new order.");
      abandonCurrentOrder();
      return;
    }

    if (!response.ok) {
      showError("Could not send your answer. Please try again.");
      return;
    }

    askPanel.hidden = true;
    askInput.value = "";
  } catch {
    showError("Something went wrong while sending your answer.");
  } finally {
    askSubmit.disabled = false;
  }
}

// The order this browser tab was tracking can never make progress again (its runId is
// unknown to the server) - clear that state so the customer isn't stuck behind a disabled
// "Place order" button forever.
function abandonCurrentOrder() {
  currentRunId = null;
  askPanel.hidden = true;
  askInput.value = "";
  submitBtn.disabled = false;
}

submitBtn.addEventListener("click", submitOrder);
askSubmit.addEventListener("click", submitAnswer);
refreshBoard();
