// WebApp click-through prototype: the design of record for WebApp/ (jev_routing_research.md §17).
// Everything below is a front-end mock of the CoffeeShopAgent AG-UI endpoint: no network calls.

const MENU = [
  { id: "CAPPUCCINO", name: "Cappuccino", price: 4.5, station: "Barista", aliases: ["cappuccino", "cappucino"] },
  { id: "COFFEE_BLACK", name: "Black coffee", price: 3.0, station: "Barista", aliases: ["black coffee", "coffee black", "americano"] },
  { id: "COFFEE_WITH_ROOM", name: "Coffee with room", price: 3.25, station: "Barista", aliases: ["coffees with room", "coffee with room"] },
  { id: "ESPRESSO", name: "Espresso", price: 3.0, station: "Barista", aliases: ["espresso", "expresso"] },
  { id: "ESPRESSO_DOUBLE", name: "Double espresso", price: 3.75, station: "Barista", aliases: ["double espresso", "doppio"] },
  { id: "LATTE", name: "Latte", price: 4.5, station: "Barista", aliases: ["latte"] },
  { id: "CAKEPOP", name: "Cake pop", price: 2.5, station: "Kitchen", aliases: ["cake pop", "cakepop"] },
  { id: "CROISSANT", name: "Croissant", price: 3.25, station: "Kitchen", aliases: ["croissant", "crossant"] },
  { id: "MUFFIN", name: "Muffin", price: 3.0, station: "Kitchen", aliases: ["muffin"] },
  { id: "CROISSANT_CHOCOLATE", name: "Chocolate croissant", price: 3.75, station: "Kitchen", aliases: ["chocolate croissant", "pain au chocolat"] },
  { id: "CHICKEN_MEATBALLS", name: "Chicken meatballs", price: 7.5, station: "Kitchen", aliases: ["chicken meatballs", "meatballs"] },
];
const BY_ID = Object.fromEntries(MENU.map((m) => [m.id, m]));
const STATION_CONF = { CHICKEN_MEATBALLS: 0.78, CAKEPOP: 0.83 }; // mid band -> "Confirm" badge
const PREP = {
  Barista: ["Grind and pull the shot", "Steam the milk", "Pour and lid"],
  Kitchen: ["Pick from the case", "Warm through", "Bag and label"],
};
const WORDS = { a: 1, an: 1, one: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7, eight: 8, nine: 9, ten: 10, "a couple of": 2, "couple of": 2, "a few": 3, "a dozen": 12, dozen: 12 };
const QW = Object.keys(WORDS).sort((a, b) => b.length - a.length).join("|");
const ALIASES = MENU.flatMap((m) => m.aliases.map((a) => [m.id, a])).sort((a, b) => b[1].length - a[1].length);
const UNKNOWN_FOOD = ["pizza", "burger", "sandwich", "tea", "juice", "bagel", "salad", "soup", "cookie", "donut", "smoothie"];
const MAX_QTY = 20;
const MAX_ASKS = 2;

// ---------- storage (per-viewer convenience only; works without it) ----------
const store = {
  get(key, fallback) { try { const v = localStorage.getItem(key); return v ? JSON.parse(v) : fallback; } catch { return fallback; } },
  set(key, value) { try { localStorage.setItem(key, JSON.stringify(value)); } catch { /* private mode: keep in memory */ } },
};

const state = {
  guestId: store.get("cs.guest", null),
  orders: store.get("cs.orders", null),
  cart: {},
  conversation: [],
  asks: 0,
  busy: false,
  scope: "mine",
  status: "all",
};

const $ = (s, el = document) => el.querySelector(s);
const $$ = (s, el = document) => [...el.querySelectorAll(s)];
const money = (n) => `$${n.toFixed(2)}`;
const sleep = (ms) => new Promise((r) => setTimeout(r, matchMedia("(prefers-reduced-motion: reduce)").matches ? 0 : ms));
const newGuestId = () => "guest-" + Math.random().toString(16).slice(2).padEnd(8, "0").slice(0, 8);
const conf = (lo, hi, seed) => +(lo + ((hash(seed) % 1000) / 1000) * (hi - lo)).toFixed(2);
function hash(s) { let h = 0; for (const c of s) h = (h * 31 + c.charCodeAt(0)) >>> 0; return h; }
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

// ---------- seed data ----------
function seed() {
  if (!state.guestId) { state.guestId = newGuestId(); store.set("cs.guest", state.guestId); }
  if (state.orders) return;
  const now = Date.now(), h = 3600e3;
  const mk = (n, who, ago, lines, status = "Completed", reason = null, message = "") => ({
    number: n, customerId: who, createdAt: now - ago, status, reason, message,
    lines: lines.map(([id, qty]) => ({ id, qty })), tickets: [], trace: null,
    total: lines.reduce((t, [id, q]) => t + BY_ID[id].price * q, 0),
    decisionModel: { intent: status === "Completed" ? "place_order" : "off_topic", confidence: 0.93 },
  });
  state.orders = [
    mk(1, "guest-7f2c", 26 * h, [["CAPPUCCINO", 1], ["MUFFIN", 1]]),
    mk(2, state.guestId, 25 * h, [["LATTE", 2], ["CROISSANT", 1]]),
    mk(3, "guest-19ab", 5 * h, [], "Rejected", "OffTopic", "I can only help with food and drink orders."),
    mk(4, state.guestId, 3 * h, [["ESPRESSO_DOUBLE", 1], ["CHICKEN_MEATBALLS", 1]]),
    mk(5, "guest-7f2c", 1 * h, [["COFFEE_WITH_ROOM", 2]]),
  ];
  store.set("cs.orders", state.orders);
}
function saveOrder(order) {
  if (run) order.trace = { conversation: [...run.conversation], provider: "prototype", model: "keyword rules", audit: run.audit, agui: run.agui };
  order.number = Math.max(0, ...state.orders.map((o) => o.number)) + 1;
  order.customerId = state.guestId;
  order.createdAt = Date.now();
  state.orders.push(order);
  store.set("cs.orders", state.orders);
  return order;
}

// ---------- per-run trace: the audit log + AG-UI events kept with the order (research §17.9) ----------
let run = null;
function audit(kind, detail) { run?.audit.push({ at: Date.now(), kind, detail }); }
const brief = (payload) => payload === undefined ? "" : (typeof payload === "string" ? payload : payload.delta ?? payload.message ?? JSON.stringify(payload));

// ---------- AG-UI event log ----------
function emit(type, payload) {
  run?.agui.push({ at: Date.now(), kind: type, detail: brief(payload).slice(0, 220) });
  const li = document.createElement("li");
  const body = payload === undefined ? "" : " " + JSON.stringify(payload);
  li.innerHTML = `<span class="ev-type${type === "RUN_ERROR" ? " err" : ""}">${type}</span>${esc(body.length > 220 ? body.slice(0, 220) + "…" : body)}`;
  $("#event-log").append(li);
  li.scrollIntoView({ block: "nearest" });
}

// ---------- the "Decision model" gate + extract (mock) ----------
function extract(text) {
  let t = " " + text.toLowerCase().replace(/[.,!?;:]/g, " ") + " ";
  if (/\bactually\b/.test(t)) t = " " + t.split(/\bactually\b/).pop() + " ";
  const found = new Map(); // id -> { qty, at }; same-length masking keeps offsets comparable across passes
  for (const [id, alias] of ALIASES) {
    const re = new RegExp(`(?:\\b(\\d+)\\s*(?:x|×)?\\s*(?:ly |cups? of |banh |cái )?|\\b(${QW})\\s+)?\\b${alias}(?:e?s)?\\b`, "g");
    t = t.replace(re, (m, num, word, at) => {
      const q = num ? +num : word ? WORDS[word] : 1;
      const prev = found.get(id);
      found.set(id, { qty: (prev?.qty || 0) + q, at: prev ? prev.at : at });
      return "#".repeat(m.length);
    });
  }
  const lines = [...found].sort((a, b) => a[1].at - b[1].at).map(([id, v]) => ({ id, qty: v.qty }));
  return { lines, rest: t };
}

function gate(text) {
  const low = text.toLowerCase();
  const seedConf = (lo, hi) => conf(lo, hi, low);
  if (/\b(weather|poem|joke|news|football|stock|homework)\b/.test(low))
    return { decision: "Rejected", reason: "OffTopic", intent: "off_topic", confidence: seedConf(0.86, 0.95), onMenu: 0.04 };
  // Proposed G1 hazard guard (JT4, not built in the real app yet): refuse instead of asking.
  if (/(ignore (all |previous |your )?(instructions|rules)|you are now|system:)/.test(low))
    return { decision: "Rejected", reason: "Blocked", intent: "other", confidence: seedConf(0.8, 0.9), onMenu: 0.2, message: "Sorry, I can only help with food and drink orders." };
  if (/\bcancel\b/.test(low))
    return { decision: "Rejected", reason: "OffTopic", intent: "other", confidence: seedConf(0.7, 0.85), onMenu: 0.1, message: "I can't change an order from here. Ask the staff at the counter." };
  if (/(order(ed)? before|(my|past|previous|last) orders|order history|last time|did i (order|have|get)|what i (ordered|had))/.test(low))
    return { decision: "HistoryRequested", intent: "order_history", confidence: seedConf(0.88, 0.96), onMenu: 0.5 };

  const { lines, rest } = extract(text);
  if (!lines.length && /(menu|what do you (have|sell|serve)|what's (available|on)|prices?|what can i (get|order))/.test(low))
    return { decision: "MenuRequested", intent: "ask_menu", confidence: seedConf(0.9, 0.97), onMenu: 0.5 };

  const unknown = UNKNOWN_FOOD.filter((w) => new RegExp(`\\b${w}s?\\b`).test(rest));
  const vagueCoffee = /\bcoffees?\b/.test(rest);
  if (lines.length && !unknown.length && !vagueCoffee)
    return { decision: "Accepted", intent: "place_order", confidence: seedConf(0.88, 0.98), onMenu: seedConf(0.9, 0.99), lines };
  if (unknown.length)
    return {
      decision: "Unclear", intent: "place_order", confidence: seedConf(0.8, 0.92), onMenu: seedConf(0.12, 0.3),
      question: lines.length
        ? `We don't have ${unknown[0]}. Shall I go ahead with just the ${lines.map((l) => BY_ID[l.id].name.toLowerCase()).join(" and ")}?`
        : `We don't have ${unknown[0]}, but the chicken meatballs and croissants are popular. What would you like instead?`,
    };
  if (vagueCoffee)
    return { decision: "Unclear", intent: "place_order", confidence: seedConf(0.62, 0.75), onMenu: 0.55, question: "Would you like your coffee black, or with room for milk?" };
  if (/\b(sweet|treat|dessert)\b/.test(low))
    return { decision: "Unclear", intent: "place_order", confidence: seedConf(0.31, 0.46), onMenu: 0.4, question: "Something sweet sounds good. Would you like a muffin, a cake pop or a chocolate croissant?" };
  return { decision: "Unclear", intent: "other", confidence: seedConf(0.22, 0.44), onMenu: 0.3, question: "What would you like? A latte and a croissant is a good place to start." };
}

// ---------- chat rendering ----------
function addMsg(who, html, kind = "") {
  $(".welcome")?.remove();
  const li = document.createElement("li");
  li.className = `msg ${who} ${kind}`;
  li.innerHTML = `<p class="who">${who === "you" ? "You" : "Counter"}</p><div class="bubble">${html}</div>`;
  $("#messages").append(li);
  li.scrollIntoView({ block: "end", behavior: "smooth" });
  return li;
}
function typing() { return addMsg("counter", `<span class="typing" aria-label="The counter is typing"><span></span><span></span><span></span></span>`); }
function welcome() {
  $("#messages").innerHTML = `<li class="welcome"><h2>What can we get started for you?</h2><p>Order the way you'd say it at the counter. Ask for the menu, or for what you ordered last time.</p></li>`;
}

// ---------- ticket rail ----------
const STEP_NAMES = ["Understand your message", "Write up the order", "Send to the stations", "Make it", "Hand it over"];
function ticketStart(label) {
  const t = $("#ticket");
  t.classList.remove("printing"); void t.offsetWidth; t.classList.add("printing");
  $("#ticket-h").textContent = label;
  $("#ticket-empty").hidden = true;
  $("#ticket-lines").hidden = true;
  $("#ticket-lines").innerHTML = "";
  const steps = $("#steps");
  steps.hidden = false;
  steps.innerHTML = STEP_NAMES.map((n) => `<li><span class="step-name">${n}</span><span class="step-detail"></span></li>`).join("");
}
function step(i, cls, detail = "") {
  const li = $$("#steps li")[i];
  if (!li) return;
  li.className = cls;
  $(".step-detail", li).textContent = detail;
}
function skipFrom(i) { $$("#steps li").slice(i).forEach((li) => { li.className = "skip"; }); }

function renderLines(lines, tickets, total, clamped) {
  const box = $("#ticket-lines");
  box.hidden = false;
  box.innerHTML = ["Barista", "Kitchen"].map((st) => {
    const mine = lines.filter((l) => BY_ID[l.id].station === st);
    const t = tickets?.[st];
    const body = mine.length
      ? mine.map((l) => `<div class="line"><span>${l.qty} × ${BY_ID[l.id].name}${l.flag ? `<span class="flag ${l.flag.toLowerCase()}">${l.flag}</span>` : ""}${t ? `<small>${t.steps.join(", ")} (${t.minutes} min)</small>` : ""}</span><span>${money(BY_ID[l.id].price * l.qty)}</span></div>`).join("")
      : `<div class="line"><small>Nothing for the ${st.toLowerCase()} on this order</small></div>`;
    return `<section class="station ${st.toLowerCase()}"><h3>${st}</h3>${body}</section>`;
  }).join("") + (total != null ? `<div class="total"><span>Total</span><span>${money(total)}</span></div>` : "")
    + (clamped ? `<p class="note">One quantity was reduced to the maximum of ${MAX_QTY}.</p>` : "");
}

// ---------- one AG-UI run ----------
async function runTurn(text) {
  if (state.busy) return;
  text = text.trim();
  if (!text) return;
  state.busy = true;
  $("#send").disabled = true;
  addMsg("you", esc(text));
  state.conversation.push(text);
  run = { audit: [], agui: [], conversation: [...state.conversation] };
  audit("message", text);
  audit("provider", "prototype / keyword rules");
  emit("RUN_STARTED", { threadId: state.guestId, messages: state.conversation.length, state: { customerId: state.guestId, asks: state.asks } });

  const dots = typing();
  await sleep(550);

  if ($("#decision-model-down").checked) {
    dots.remove();
    const msg = "We can't take orders right now, please try again in a moment.";
    audit("gate-error", "prototype / keyword rules: HTTP 503 (simulated outage)");
    audit("result", `Rejected (DecisionModelDown) · total 0.00 · ${msg}`);
    emit("STATE_SNAPSHOT", { order_progress: { status: "Rejected" } });
    emit("TEXT_MESSAGE_CONTENT", { role: "assistant", delta: msg });
    addMsg("counter", msg, "err");
    saveOrder({ status: "Rejected", reason: "DecisionModelDown", message: msg, lines: [], tickets: [], total: 0, decisionModel: null });
    endRun(true);
    return;
  }

  const g = gate(text);
  audit("gate", `${g.decision} · intent ${g.intent} ${g.confidence} · on menu ${g.onMenu} · model prototype-rules`);
  emit("STATE_SNAPSHOT", { order_progress: { gate: { decision: g.decision, intent: g.intent, confidence: g.confidence, on_menu: g.onMenu } } });

  if (g.decision === "MenuRequested") {
    dots.remove();
    emit("TOOL_CALL_START", { toolCallName: "show_menu" });
    emit("TOOL_CALL_RESULT", { items: MENU.length });
    const node = addMsg("counter", "Here's what we have today.");
    const card = $("#tpl-menu-card").content.cloneNode(true);
    $(".mini-menu", card).innerHTML = MENU.map((m) => `<li><span>${m.name}</span><span>${money(m.price)}</span></li>`).join("");
    $(".bubble", node).append(card);
    return endRun(false);
  }

  if (g.decision === "HistoryRequested") {
    dots.remove();
    const mine = myOrders().filter((o) => o.status === "Completed");
    emit("TOOL_CALL_START", { toolCallName: "show_history" });
    emit("TOOL_CALL_RESULT", { customerId: state.guestId, orders: mine.length });
    if (!mine.length) {
      addMsg("counter", `You haven't ordered anything yet. Want to see the menu? <a class="card-link" href="#/menu">Open the menu</a>`);
    } else {
      const rows = mine.slice(0, 4).map((o) => `<li><span>#${o.number} ${summary(o.lines)}</span><span>${money(o.total)}</span></li>`).join("");
      addMsg("counter", `Here's what you've ordered, newest first.<div class="card"><ul class="mini-history">${rows}</ul><a class="card-link" href="#/orders">See all your orders</a></div>`);
    }
    return endRun(false);
  }

  // Order flow: print a ticket and walk the steps.
  ticketStart(state.asks ? "Current order (continued)" : "Current order");
  step(0, "done", `Decision model: ${g.intent} ${g.confidence}, on menu ${g.onMenu}`);

  if (g.decision === "Rejected") {
    dots.remove();
    step(0, "fail", `Decision model: ${g.intent} ${g.confidence}. Not an order.`);
    skipFrom(1);
    const msg = g.message || "I can only help with food and drink orders.";
    addMsg("counter", esc(msg));
    audit("result", `Rejected (${g.reason}) · total 0.00 · ${msg}`);
    emit("TEXT_MESSAGE_CONTENT", { role: "assistant", delta: msg });
    saveOrder({ status: "Rejected", reason: g.reason, message: msg, lines: [], tickets: [], total: 0, decisionModel: { intent: g.intent, confidence: g.confidence } });
    return endRun(true);
  }

  if (g.decision === "Unclear") {
    dots.remove();
    if (state.asks >= MAX_ASKS) {
      step(0, "fail", `Asked ${MAX_ASKS} times and still unclear`);
      skipFrom(1);
      const msg = "I still couldn't work out your order, so I've stopped here. Start again whenever you're ready.";
      addMsg("counter", msg, "err");
      audit("result", `Rejected (AskCap) · total 0.00 · ${msg}`);
      emit("TEXT_MESSAGE_CONTENT", { role: "assistant", delta: msg });
      saveOrder({ status: "Rejected", reason: "AskCap", message: msg, lines: [], tickets: [], total: 0, decisionModel: { intent: g.intent, confidence: g.confidence } });
      return endRun(true);
    }
    state.asks += 1;
    step(0, "active", `Needs one detail. Asked you (${state.asks} of ${MAX_ASKS})`);
    skipFrom(1);
    audit("clarify", g.question);
    emit("TEXT_MESSAGE_CONTENT", { role: "assistant", delta: g.question });
    addMsg("counter", esc(g.question), "ask");
    $("#hint").textContent = "Answer in your next message. The counter keeps the conversation so far.";
    return endRun(false); // turn boundary: the run ends, the conversation carries over
  }

  // Accepted
  step(1, "active");
  await sleep(600);
  const lines = g.lines.map((l) => ({ ...l, clamped: l.qty > MAX_QTY, qty: Math.min(l.qty, MAX_QTY) }));
  const clamped = lines.some((l) => l.clamped);
  step(1, "done", `${lines.length} line${lines.length > 1 ? "s" : ""}, names checked against the menu`);
  renderLines(lines);
  emit("STATE_SNAPSHOT", { order_progress: { lines: lines.map(({ id, qty }) => ({ id, qty })) } });

  step(2, "active");
  await sleep(500);
  for (const l of lines) {
    const c = STATION_CONF[l.id] ?? conf(0.88, 0.98, l.id);
    l.conf = c;
    l.flag = c < 0.6 ? "Review" : c <= 0.85 ? "Confirm" : null;
  }
  const flagged = lines.filter((l) => l.flag).length;
  step(2, "done", `Decision model picked a station per line${flagged ? `, ${flagged} flagged for staff to confirm` : ""}`);
  renderLines(lines);
  audit("split", lines.map((l) => `${l.qty}× ${l.id} → ${BY_ID[l.id].station} (${l.flag || "None"})`).join("; "));
  emit("STATE_SNAPSHOT", { order_progress: { split: lines.map(({ id, conf: c, flag }) => ({ id, station: BY_ID[id].station, confidence: c, flag })) } });

  step(3, "active", "Barista and kitchen working in parallel");
  await sleep(800);
  const tickets = {};
  for (const st of ["Barista", "Kitchen"]) {
    const mine = lines.filter((l) => BY_ID[l.id].station === st);
    if (mine.length) tickets[st] = { steps: PREP[st], minutes: Math.min(2 + mine.reduce((t, l) => t + l.qty, 0), 9) };
  }
  step(3, "done", Object.keys(tickets).map((s) => `${s} ${tickets[s].minutes} min`).join(", "));
  const total = lines.reduce((t, l) => t + BY_ID[l.id].price * l.qty, 0);
  renderLines(lines, tickets, total, clamped);
  emit("STATE_SNAPSHOT", { order_progress: { tickets, total } });

  step(4, "active");
  await sleep(500);
  dots.remove();
  for (const [st, t] of Object.entries(tickets)) audit("ticket", `${st}: ${lines.filter((l) => BY_ID[l.id].station === st).map((l) => `${l.qty}× ${l.id}`).join(", ")}`);
  const reply = `Thanks! ${summary(lines)} ${lines.length > 1 || lines[0].qty > 1 ? "are" : "is"} being made now. That's ${money(total)}.${clamped ? ` I've reduced one quantity to our maximum of ${MAX_QTY}.` : ""}`;
  audit("result", `Completed · total ${total.toFixed(2)} · ${reply}`);
  emit("TEXT_MESSAGE_CONTENT", { role: "assistant", delta: reply });
  addMsg("counter", esc(reply));
  const tix = Object.entries(tickets).map(([station, t]) => ({ station, items: lines.filter((l) => BY_ID[l.id].station === station).map((l) => ({ id: l.id, qty: l.qty, steps: t.steps, minutes: t.minutes })) }));
  const saved = saveOrder({ status: "Completed", reason: null, message: reply, lines: lines.map(({ id, qty, flag }) => ({ id, qty, flag })), tickets: tix, total, decisionModel: { intent: g.intent, confidence: g.confidence } });
  step(4, "done", `Order #${saved.number}`);
  $("#ticket-h").textContent = `Order #${saved.number}`;
  endRun(true);
}

function endRun(closeConversation) {
  emit("RUN_FINISHED", { outcome: closeConversation ? "complete" : "awaiting_next_turn" });
  if (closeConversation && run) store.set("cs.orders", state.orders); // the trace keeps the arrays, so RUN_FINISHED lands in the saved order
  run = null;
  if (closeConversation) { state.conversation = []; state.asks = 0; $("#hint").textContent = ""; }
  state.busy = false;
  $("#send").disabled = false;
  $("#say").focus();
}

function summary(lines) {
  if (!lines.length) return "nothing";
  const parts = lines.map((l) => `${l.qty} ${BY_ID[l.id].name.toLowerCase()}${l.qty > 1 && !/s$/.test(BY_ID[l.id].name) ? "s" : ""}`);
  return parts.length > 1 ? parts.slice(0, -1).join(", ") + " and " + parts.at(-1) : parts[0];
}

// ---------- menu board + cart ----------
function renderBoard() {
  const col = (st, sub) => `<div class="board-col"><h2>${st === "Barista" ? "From the bar" : "From the kitchen"}</h2><p class="sub">${sub}</p><ul class="items">${
    MENU.filter((m) => m.station === st).map((m) => {
      const q = state.cart[m.id] || 0;
      return `<li class="item${q ? " in-cart" : ""}" data-id="${m.id}">
        <span class="item-name">${m.name}</span><span class="item-price">${money(m.price)}</span>
        <div class="stepper"><button type="button" data-d="-1" aria-label="Remove one ${m.name}" ${q ? "" : "disabled"}>−</button><output aria-live="polite">${q}</output><button type="button" data-d="1" aria-label="Add one ${m.name}">+</button></div>
      </li>`;
    }).join("")
  }</ul></div>`;
  $("#board").innerHTML = col("Barista", "Made by the barista") + col("Kitchen", "Made in the kitchen");
  renderCart();
}
function renderCart() {
  const entries = Object.entries(state.cart).filter(([, q]) => q > 0);
  const count = entries.reduce((t, [, q]) => t + q, 0);
  const total = entries.reduce((t, [id, q]) => t + BY_ID[id].price * q, 0);
  $("#cartbar").hidden = !count;
  $(".cart-count").hidden = !count;
  $(".cart-count").textContent = count;
  $("#cart-summary").textContent = `${count} item${count === 1 ? "" : "s"} in your order, ${money(total)}`;
}

// ---------- history ----------
function myOrders() { return state.orders.filter((o) => o.customerId === state.guestId).sort((a, b) => b.number - a.number); }
function renderOrders() {
  const base = state.scope === "mine" ? myOrders() : [...state.orders].sort((a, b) => b.number - a.number);
  const list = base.filter((o) => state.status === "all" || o.status === state.status);
  $("#orders-h").textContent = state.scope === "mine" ? "Your orders" : "Everyone's orders today";
  $("#orders-empty").hidden = list.length > 0;
  $("#orders-empty p").textContent = state.scope === "mine" && state.status === "all" ? "You haven't ordered anything yet." : "No orders match this filter.";
  $("#slips").innerHTML = list.map((o) => {
    const when = new Date(o.createdAt).toLocaleString([], { weekday: "short", hour: "2-digit", minute: "2-digit" });
    const lines = o.lines.length
      ? o.lines.map((l) => `<div class="line"><span>${l.qty} × ${BY_ID[l.id].name}${l.flag ? `<span class="flag ${l.flag.toLowerCase()}">${l.flag}</span>` : ""}</span><span>${money(BY_ID[l.id].price * l.qty)}</span></div>`).join("")
        + `<div class="total"><span>Total</span><span>${money(o.total)}</span></div>`
      : `<div class="line"><small>${esc(o.message || "No items")}</small></div>`;
    const reasons = { OffTopic: "Not a food or drink order", AskCap: "Stopped after two questions", DecisionModelDown: "Ordering was offline", Blocked: "Blocked by a guard" };
    return `<li class="slip ${o.status.toLowerCase()}" data-testid="history-row">
      <div class="slip-top"><a class="slip-no" href="#/orders/${o.number}" data-testid="order-link">#${o.number}</a><span class="status ${o.status}">${o.status === "Completed" ? "Completed" : "Not placed"}</span></div>
      <p class="slip-time">${when}${state.scope === "all" ? ` <span class="slip-who">${o.customerId === state.guestId ? "you" : esc(o.customerId)}</span>` : ""}</p>
      <div class="slip-lines">${lines}</div>
      <a class="card-link slip-more" href="#/orders/${o.number}" data-testid="order-details-link">Order details, audit log and AG-UI events</a>
      <details><summary>How the counter handled it</summary>
        ${o.decisionModel ? `<p>Decision model read the message as ${o.decisionModel.intent} (confidence ${o.decisionModel.confidence}).</p>` : "<p>Decision model was not reachable.</p>"}
        ${o.reason ? `<p>${reasons[o.reason] || o.reason}.</p>` : `<p>Lines checked against the menu; prices from the catalog.</p>`}
      </details>
    </li>`;
  }).join("");
}

// ---------- order details: receipt + audit log + AG-UI events + raw record ----------
function renderDetail(n) {
  const o = state.orders.find((x) => x.number === n);
  const box = $("#detail");
  if (!o) { box.innerHTML = `<p class="empty" data-testid="detail-missing">There is no order #${n}. It may be from before the last restart.</p>`; return; }
  const ok = o.status === "Completed", tr = o.trace;
  const t0 = tr ? Math.min(...[...tr.audit, ...tr.agui].map((e) => e.at)) : o.createdAt;
  const off = (at) => "+" + ((at - t0) / 1000).toFixed(3) + "s";
  const rows = (list, cls) => list.map((e) => `<li><span class="log-t">${off(e.at)}</span><span class="${cls ? "ev-type" + (e.kind === "RUN_ERROR" ? " err" : "") : "log-k"}">${esc(e.kind)}</span><span class="log-d">${esc(e.detail)}</span></li>`).join("");
  const lines = o.lines.length
    ? o.lines.map((l) => `<div class="line"><span>${l.qty} × ${BY_ID[l.id].name}${l.flag ? `<span class="flag ${l.flag.toLowerCase()}">${l.flag}</span>` : ""}</span><span>${money(BY_ID[l.id].price * l.qty)}</span></div>`).join("") + `<div class="total"><span>Total</span><span>${money(o.total)}</span></div>`
    : `<div class="line"><small>No items</small></div>`;
  const tix = (o.tickets || []).map((t) => `<section class="station ${t.station.toLowerCase()}"><h3>${t.station}</h3>${t.items.map((i) => `<div class="line"><span>${i.qty} × ${BY_ID[i.id].name}<small>${i.steps.join(", ")} (${i.minutes} min)</small></span></div>`).join("")}</section>`).join("");
  box.innerHTML = `
    <p><a class="card-link" href="#/orders" data-testid="back-to-orders">← Your orders</a></p>
    <div class="orders-head"><h1 id="detail-h">Order #${o.number}</h1><span class="status ${o.status}" data-testid="detail-status">${ok ? "Completed" : "Not placed"}</span></div>
    <p class="slip-time" data-testid="detail-meta">${new Date(o.createdAt).toLocaleString([], { weekday: "short", day: "numeric", month: "short", hour: "2-digit", minute: "2-digit", second: "2-digit" })} · customer ${esc(o.customerId)}${tr ? ` · decision model ${tr.provider} / ${tr.model}` : ""}</p>
    <div class="detail-grid">
      <article class="slip detail-slip" data-testid="detail-receipt">
        <div class="slip-lines">${lines}</div>${tix}
        <p class="note">${esc(o.message)}</p>${o.reason ? `<p class="note">Reason: ${esc(o.reason)}</p>` : ""}
        ${tr?.conversation.length ? `<h3 class="detail-h">Conversation</h3><ol class="convo" data-testid="detail-conversation">${tr.conversation.map((m) => `<li>${esc(m)}</li>`).join("")}</ol>` : ""}
      </article>
      <div class="detail-logs">
        ${tr ? `<h2 class="detail-h">Audit log</h2><ol class="log" data-testid="audit-log">${rows(tr.audit, false)}</ol>
        <h2 class="detail-h">AG-UI events</h2><ol class="log log-dark" data-testid="agui-log">${rows(tr.agui, true)}</ol>`
          : `<p class="empty" data-testid="detail-no-trace">No audit log was recorded for this order (seed data, like an order placed through the legacy endpoint).</p>`}
        <details class="raw" data-testid="detail-raw"><summary>Raw record (JSON)</summary><pre>${esc(JSON.stringify(o, null, 2))}</pre></details>
      </div>
    </div>`;
}

// ---------- router + wiring ----------
function route() {
  const m = location.hash.match(/^#\/(\w+)(?:\/(\d+))?/) || [, "order"];
  const name = ["order", "menu", "orders"].includes(m[1]) ? m[1] : "order";
  const view = name === "orders" && m[2] ? "detail" : name;
  $$(".view").forEach((v) => { v.hidden = v.dataset.view !== view; });
  if (view === "detail") renderDetail(+m[2]);
  $$(".tabs a").forEach((a) => (a.dataset.route === name ? a.setAttribute("aria-current", "page") : a.removeAttribute("aria-current")));
  if (name === "menu") renderBoard();
  if (name === "orders") renderOrders();
  if (name === "order") $("#say").focus();
}

function wire() {
  $("#guest-label").textContent = state.guestId;
  $("#guest-id").textContent = state.guestId;
  welcome();

  $("#composer").addEventListener("submit", (e) => { e.preventDefault(); const t = $("#say").value; $("#say").value = ""; runTurn(t); });
  $("#say").addEventListener("keydown", (e) => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); $("#composer").requestSubmit(); } });
  $("#suggest").addEventListener("click", (e) => { const b = e.target.closest("button"); if (b) runTurn(b.dataset.say); });

  $("#board").addEventListener("click", (e) => {
    const b = e.target.closest("button[data-d]"); if (!b) return;
    const id = b.closest(".item").dataset.id;
    state.cart[id] = Math.max(0, Math.min(MAX_QTY, (state.cart[id] || 0) + +b.dataset.d));
    renderBoard();
    $(`.item[data-id="${id}"] button[data-d="${b.dataset.d}"]`)?.focus();
  });
  $("#cart-clear").addEventListener("click", () => { state.cart = {}; renderBoard(); });
  $("#cart-place").addEventListener("click", () => {
    const text = Object.entries(state.cart).filter(([, q]) => q).map(([id, q]) => `${q} × ${BY_ID[id].name}`).join(", ");
    state.cart = {};
    renderCart();
    location.hash = "#/order";
    setTimeout(() => runTurn(text), 50);
  });

  $$(".seg button").forEach((b) => b.addEventListener("click", () => {
    state.scope = b.dataset.scope;
    $$(".seg button").forEach((x) => x.setAttribute("aria-checked", String(x === b)));
    renderOrders();
  }));
  $$(".filters .chip").forEach((b) => b.addEventListener("click", () => {
    state.status = b.dataset.status;
    $$(".filters .chip").forEach((x) => x.classList.toggle("on", x === b));
    renderOrders();
  }));

  const pop = $("#guest-pop"), gb = $("#guest-btn");
  gb.addEventListener("click", () => { pop.hidden = !pop.hidden; gb.setAttribute("aria-expanded", String(!pop.hidden)); });
  document.addEventListener("click", (e) => { if (!pop.hidden && !e.target.closest(".tools")) { pop.hidden = true; gb.setAttribute("aria-expanded", "false"); } });
  document.addEventListener("keydown", (e) => { if (e.key === "Escape" && !pop.hidden) { pop.hidden = true; gb.setAttribute("aria-expanded", "false"); gb.focus(); } });
  $("#new-guest").addEventListener("click", () => {
    state.guestId = newGuestId(); store.set("cs.guest", state.guestId);
    $("#guest-label").textContent = state.guestId; $("#guest-id").textContent = state.guestId;
    state.conversation = []; state.asks = 0; welcome();
    $("#ticket-empty").hidden = false; $("#steps").hidden = true; $("#ticket-lines").hidden = true; $("#ticket-h").textContent = "Current order";
    route();
  });
  // Theme: starts from the system setting; a click pins light or dark and is remembered.
  const darkQuery = matchMedia("(prefers-color-scheme: dark)");
  const isDark = () => (document.documentElement.dataset.theme || (darkQuery.matches ? "dark" : "light")) === "dark";
  const syncThemeBtn = () => $("#theme-btn").setAttribute("aria-pressed", String(isDark()));
  $("#theme-btn").addEventListener("click", () => {
    const next = isDark() ? "light" : "dark";
    document.documentElement.dataset.theme = next;
    store.set("cs.theme", next);
    syncThemeBtn();
  });
  darkQuery.addEventListener("change", syncThemeBtn);
  syncThemeBtn();

  $("#decision-model-down").addEventListener("change", (e) => document.body.classList.toggle("decision-model-down", e.target.checked));
  $("#show-events").addEventListener("change", (e) => { $("#events").hidden = !e.target.checked; });
  $("#events-clear").addEventListener("click", () => { $("#event-log").innerHTML = ""; });

  addEventListener("hashchange", route);
  route();
}

seed();
wire();
