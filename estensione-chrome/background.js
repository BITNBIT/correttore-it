// Correttore IT - service worker: unico punto che parla con il motore locale su questo PC.
const API = 'http://127.0.0.1:47821';

const DEFAULTS = { enabled: true, mode: 'all', allow: [], block: [], suggest: true };

chrome.runtime.onInstalled.addListener(async () => {
  const cur = await chrome.storage.local.get(Object.keys(DEFAULTS));
  const missing = {};
  for (const k of Object.keys(DEFAULTS)) if (cur[k] === undefined) missing[k] = DEFAULTS[k];
  if (Object.keys(missing).length) await chrome.storage.local.set(missing);
  setupMenus();
});
chrome.runtime.onStartup.addListener(setupMenus);

// tasto destro: "Correttore IT" con tutte le modalità
const MODES = [
  ['fix', 'Correggi'],
  ['completa', 'Completa la frase'],
  ['rispondi', 'Scrivi la risposta'],
  ['formale', 'Più formale'],
  ['semplice', 'Più semplice e breve'],
  ['traduci', 'Traduci']
];

function setupMenus() {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({ id: 'cit-root', title: 'Correttore IT', contexts: ['selection', 'editable'] });
    for (const [id, title] of MODES) {
      chrome.contextMenus.create({ id: 'cit-' + id, parentId: 'cit-root', title, contexts: ['selection', 'editable'] });
    }
  });
}

chrome.contextMenus.onClicked.addListener((info, tab) => {
  const id = String(info.menuItemId || '');
  if (!id.startsWith('cit-') || id === 'cit-root' || !tab || tab.id === undefined) return;
  chrome.tabs.sendMessage(tab.id, { type: 'cit-ctx', mode: id.slice(4), selectionText: info.selectionText || '' },
    { frameId: info.frameId || 0 }, () => void chrome.runtime.lastError);
});

async function call(path, body, timeoutMs) {
  const ctrl = new AbortController();
  const t = setTimeout(() => ctrl.abort(), timeoutMs);
  try {
    const res = await fetch(API + path, {
      method: body ? 'POST' : 'GET',
      headers: body ? { 'Content-Type': 'application/json' } : undefined,
      body: body ? JSON.stringify(body) : undefined,
      signal: ctrl.signal,
      cache: 'no-store'
    });
    let data = null;
    try { data = await res.json(); } catch (e) { data = null; }
    if (!res.ok) return { ok: false, error: (data && data.error) || ('Errore ' + res.status) };
    return { ok: true, data };
  } catch (e) {
    if (e.name === 'AbortError') return { ok: false, error: 'Tempo scaduto: il testo è molto lungo o il PC è occupato.' };
    return { ok: false, offline: true, error: 'Motore locale non raggiungibile: avvia "Correttore IT" su Windows (menu Start).' };
  } finally {
    clearTimeout(t);
  }
}

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (!msg || typeof msg.type !== 'string') return;
  if (msg.type === 'cit-correct') {
    call('/correct', { text: String(msg.text || '') }, 280000).then(sendResponse);
    return true;
  }
  if (msg.type === 'cit-status') {
    call('/status', null, 2500).then(sendResponse);
    return true;
  }
  if (msg.type === 'cit-settings') {
    call('/impostazioni', { lingua: msg.lingua || null, traduciIn: msg.traduciIn || null }, 5000).then(sendResponse);
    return true;
  }
  if (msg.type === 'cit-suggest') {
    call('/suggest', { before: String(msg.before || ''), context: String(msg.context || '') }, 20000).then(sendResponse);
    return true;
  }
});

// risposta suggerita: il testo arriva a pezzi mentre il motore lo scrive
chrome.runtime.onConnect.addListener((port) => {
  if (port.name !== 'cit-reply') return;
  const ctrl = new AbortController();
  port.onDisconnect.addListener(() => ctrl.abort());
  port.onMessage.addListener(async (msg) => {
    const post = (m) => { try { port.postMessage(m); } catch (e) { ctrl.abort(); } };
    try {
      const res = await fetch(API + '/reply', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          mode: String(msg.mode || 'rispondi'),
          text: String(msg.text || ''),
          context: String(msg.context || ''),
          draft: String(msg.draft || ''),
          temperature: msg.temperature || 0.4
        }),
        signal: ctrl.signal,
        cache: 'no-store'
      });
      if (!res.ok) {
        let data = null;
        try { data = await res.json(); } catch (e) { }
        post({ error: (data && data.error) || ('Errore ' + res.status) });
        return;
      }
      const reader = res.body.getReader();
      const dec = new TextDecoder();
      for (;;) {
        const { value, done } = await reader.read();
        if (done) break;
        const t = dec.decode(value, { stream: true });
        if (t) post({ chunk: t });
      }
      post({ done: true });
    } catch (e) {
      if (e.name !== 'AbortError') post({ error: 'Motore locale non raggiungibile: avvia "Correttore IT" su Windows (menu Start).' });
    }
  });
});
