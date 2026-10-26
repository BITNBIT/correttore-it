const $ = (id) => document.getElementById(id);
let state = { enabled: true, mode: 'all', allow: [], block: [], suggest: true };
let host = '';

const save = () => chrome.storage.local.set(state);

const render = () => {
  $('enabled').checked = !!state.enabled;
  $('suggest').checked = state.suggest !== false;
  $('mode').value = state.mode;
  const list = state.mode === 'allowlist' ? state.allow : state.block;
  $('listTitle').textContent = state.mode === 'allowlist' ? 'Siti dove il pulsante è attivo' : 'Siti esclusi';
  const ul = $('list');
  ul.textContent = '';
  if (!list.length) {
    const li = document.createElement('li'); li.className = 'empty';
    li.textContent = state.mode === 'allowlist' ? 'Nessun sito: attivalo dall’interruttore qui sopra' : 'Nessuno';
    ul.append(li);
  }
  for (const h of list) {
    const li = document.createElement('li');
    const s = document.createElement('span'); s.textContent = h;
    const b = document.createElement('button'); b.textContent = '×'; b.title = 'Rimuovi';
    b.onclick = () => {
      if (state.mode === 'allowlist') state.allow = state.allow.filter((x) => x !== h);
      else state.block = state.block.filter((x) => x !== h);
      save(); render();
    };
    li.append(s, b); ul.append(li);
  }
  const row = $('siteRow');
  if (!host) { row.classList.add('disabled'); $('host').textContent = 'questa pagina (non supportata)'; $('site').checked = false; }
  else {
    row.classList.toggle('disabled', !state.enabled);
    $('host').textContent = host;
    $('site').checked = state.mode === 'allowlist' ? state.allow.includes(host) : !state.block.includes(host);
  }
};

$('enabled').onchange = (e) => { state.enabled = e.target.checked; save(); render(); };
$('suggest').onchange = (e) => { state.suggest = e.target.checked; save(); render(); };
$('mode').onchange = (e) => { state.mode = e.target.value; save(); render(); };
$('site').onchange = (e) => {
  if (!host) return;
  const on = e.target.checked;
  if (state.mode === 'allowlist') state.allow = on ? [...new Set([...state.allow, host])] : state.allow.filter((x) => x !== host);
  else state.block = on ? state.block.filter((x) => x !== host) : [...new Set([...state.block, host])];
  save(); render();
};

const setStatus = (cls, text) => { $('status').className = 'status ' + cls; $('statusText').textContent = text; };

chrome.runtime.sendMessage({ type: 'cit-status' }, (res) => {
  if (chrome.runtime.lastError || !res) return setStatus('bad', 'Errore interno dell’estensione');
  if (!res.ok) return setStatus('bad', 'Motore non raggiungibile: avvia “Correttore IT” dal menu Start di Windows.');
  const d = res.data || {};
  if (d.modello) $('model').textContent = d.modello + ' · llama.cpp';
  // lingue: le stesse del programma Windows (un'unica impostazione per tutto il PC)
  if (Array.isArray(d.lingue)) {
    for (const [id, key] of [['lingua', 'lingua'], ['traduciIn', 'traduciIn']]) {
      const sel = $(id);
      sel.textContent = '';
      for (const [code, name] of d.lingue) {
        const o = document.createElement('option');
        o.value = code;
        o.textContent = name.charAt(0).toUpperCase() + name.slice(1);
        sel.append(o);
      }
      sel.value = d[key] || (id === 'lingua' ? 'it' : 'en');
      sel.disabled = false;
      sel.onchange = () => chrome.runtime.sendMessage({ type: 'cit-settings', [key]: sel.value }, () => void chrome.runtime.lastError);
    }
  }
  if (!d.attivo) return setStatus('warn', 'Disattivato da Windows: riattivalo dall’icona vicino all’orologio.');
  if (d.stato === 'pronto') return setStatus('ok', 'Motore AI pronto');
  if (d.stato === 'avvio') return setStatus('warn', 'Avvio del motore AI…');
  if (d.stato === 'errore') return setStatus('bad', 'Errore del motore: riavvia Correttore IT.');
  setStatus('ok', 'Pronto (il motore si avvia alla prima correzione)');
});

chrome.storage.local.get(['enabled', 'mode', 'allow', 'block', 'suggest'], (s) => {
  state = Object.assign(state, s || {});
  state.allow = state.allow || [];
  state.block = state.block || [];
  chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
    const tab = tabs && tabs[0];
    if (!tab || tab.id === undefined) return render();
    chrome.tabs.sendMessage(tab.id, { type: 'cit-host' }, { frameId: 0 }, (r) => {
      void chrome.runtime.lastError;
      host = (r && r.host) || '';
      render();
    });
  });
});
