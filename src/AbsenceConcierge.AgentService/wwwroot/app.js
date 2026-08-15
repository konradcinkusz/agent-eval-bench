// The showcase page.
//
// Two rules it keeps, and both are the same rule the service keeps one layer down:
//
//   1. NOTHING IS PARSED OUT OF THE PROSE. The confirmation card is rendered from
//      the structured draft the service returns, which carries the same values the
//      trace recorded. A page that read dates back out of the reply would be a
//      second implementation of the draft, free to disagree with the one a human is
//      about to approve.
//
//   2. EVERY VALUE IS SET WITH textContent. Never innerHTML. Leave type names and
//      employee names come from fixtures that deliberately contain injection
//      attempts — adv-003 hides an instruction inside a leave type name — and a
//      page that renders them as markup would be the one place in this repository
//      where that content becomes executable.

const turns = document.getElementById('turns');
const card = document.getElementById('card');
const banner = document.getElementById('banner');
const form = document.getElementById('composer');
const message = document.getElementById('message');
const send = document.getElementById('send');
const codeField = document.getElementById('code');

// One conversation per page load. A confirmation only resolves a draft held by the
// same conversation, so a reload is a clean slate rather than a way back into one.
const conversationId = `web-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;

let accessCode = '';
let busy = false;

function headers() {
  const h = { 'Content-Type': 'application/json' };
  if (accessCode) h['X-Demo-Access-Code'] = accessCode;
  return h;
}

function append(who, text, className) {
  const li = document.createElement('li');
  if (className) li.className = className;

  const label = document.createElement('span');
  label.className = 'who';
  label.textContent = who;

  li.append(label, document.createTextNode(text));
  turns.append(li);
  li.scrollIntoView({ block: 'nearest' });
}

function setBanner(mode) {
  banner.textContent = mode.reason;
  banner.classList.toggle('live', Boolean(mode.live));

  if (typeof mode.remaining === 'number') {
    banner.textContent += ` (${mode.remaining.toLocaleString()} output tokens left today)`;
  }
}

function set(id, value) {
  document.getElementById(id).textContent = value;
}

function showCard(confirmation, degradations) {
  set('card-type', confirmation.leaveTypeName);
  set('card-dates', confirmation.startDate === confirmation.endDate
    ? confirmation.startDate
    : `${confirmation.startDate} to ${confirmation.endDate}`);
  set('card-days', String(confirmation.workingDays));

  const excluded = confirmation.excludedDays ?? [];
  document.getElementById('card-excluded-row').hidden = excluded.length === 0;
  set('card-excluded', excluded.join(', '));

  document.getElementById('card-attachment-row').hidden = !confirmation.attachmentRequired;
  set('card-attachment', 'A medical certificate, because of how long this is.');

  // `not_run` is shown rather than hidden. Whether existing bookings were actually
  // checked is a fact somebody needs before approving, not an internal detail.
  set('card-conflicts', {
    clean: 'Checked — nothing overlaps',
    conflicts_found: 'Checked — something overlaps',
    not_run: 'NOT CHECKED — the lookup failed, so an overlap is unknown',
  }[confirmation.conflictCheck] ?? confirmation.conflictCheck);

  const degraded = document.getElementById('card-degraded');
  degraded.hidden = !degradations || degradations.length === 0;

  if (!degraded.hidden) {
    degraded.textContent = `Drafted without: ${degradations.map((d) => d.phase).join(', ')}.`;
  }

  card.hidden = false;
}

function lock(on) {
  busy = on;
  send.disabled = on;
  document.getElementById('approve').disabled = on;
  document.getElementById('reject').disabled = on;
}

async function turn(text, decision) {
  if (busy) return;
  lock(true);
  card.hidden = true;

  try {
    const response = await fetch('/agent/turn', {
      method: 'POST',
      headers: headers(),
      body: JSON.stringify({ conversationId, message: text, decision: decision ?? null }),
    });

    if (response.status === 429) {
      append('service', 'Too many requests from this address. Wait a minute and try again.');
      return;
    }

    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      append('service', body.error ?? `The service answered ${response.status}.`);
      return;
    }

    const { turn: result, mode } = await response.json();

    setBanner(mode);
    append('agent', result.reply);

    if (result.confirmation) {
      showCard(result.confirmation, result.degradations);
    }
  } catch {
    // Deliberately not showing the exception. A network error's message is about
    // this browser, not about the agent, and putting it in the transcript reads as
    // something the agent said.
    append('service', 'The service could not be reached.');
  } finally {
    lock(false);
  }
}

form.addEventListener('submit', (event) => {
  event.preventDefault();
  const text = message.value.trim();
  if (!text) return;

  append('you', text, 'said');
  message.value = '';
  turn(text, null);
});

// Approve and reject are buttons, not sentences. The decision travels as a typed
// field so that no amount of persuasive text can stand in for it — which is the same
// reason the scenario schema gives a confirmation its own role.
document.getElementById('approve').addEventListener('click', () => {
  append('you', 'Yes, submit it', 'said');
  turn('Yes, submit it', 'approve');
});

document.getElementById('reject').addEventListener('click', () => {
  append('you', 'No, cancel', 'said');
  turn('No, cancel', 'reject');
});

document.querySelectorAll('button[data-say]').forEach((button) => {
  button.addEventListener('click', () => {
    message.value = button.dataset.say;
    message.focus();
  });
});

document.getElementById('unlock').addEventListener('click', async () => {
  accessCode = codeField.value.trim();
  const response = await fetch('/demo/status', { headers: headers() });
  if (response.ok) setBanner(await response.json());
});

fetch('/demo/status')
  .then((response) => (response.ok ? response.json() : null))
  .then((mode) => {
    if (mode) setBanner(mode);
  })
  .catch(() => {
    banner.textContent = 'Replies are written by the deterministic composer.';
  });
