// Correttore IT - pulsante di correzione nei campi di testo + pannello con le modifiche evidenziate.
(() => {
  if (window.__correttoreIT) return;
  window.__correttoreIT = true;

  const HOST = location.hostname || '';
  const MAX_CHARS = 20000;
  let settings = { enabled: true, mode: 'all', allow: [], block: [], suggest: true };
  let allowed = false;

  const computeAllowed = () => {
    if (!settings.enabled || !HOST) return false;
    if (settings.mode === 'allowlist') return (settings.allow || []).includes(HOST);
    return !(settings.block || []).includes(HOST);
  };

  const loadSettings = () => {
    try {
      chrome.storage.local.get(['enabled', 'mode', 'allow', 'block', 'suggest'], (s) => {
        settings = Object.assign(settings, s || {});
        if (settings.suggest === undefined) settings.suggest = true;
        allowed = computeAllowed();
        if (!allowed) hideButton();
        if (!settings.suggest) hideGhost();
      });
    } catch (e) { /* estensione ricaricata */ }
  };
  loadSettings();
  try { chrome.storage.onChanged.addListener(loadSettings); } catch (e) { }

  // ---------------------------------------------------------------- campi modificabili
  const deepActive = () => {
    let a = document.activeElement;
    while (a && a.shadowRoot && a.shadowRoot.activeElement) a = a.shadowRoot.activeElement;
    return a;
  };

  const editableFrom = (el) => {
    if (!el || el.nodeType !== 1) return null;
    if (el.closest && el.closest('cit-root')) return null;
    const tag = el.tagName;
    if (tag === 'TEXTAREA') return el.readOnly || el.disabled ? null : { kind: 'value', el };
    if (tag === 'INPUT') {
      const t = (el.getAttribute('type') || 'text').toLowerCase();
      if (!['text', 'search', ''].includes(t) || el.readOnly || el.disabled) return null;
      return { kind: 'value', el };
    }
    if (el.isContentEditable) {
      let r = el;
      while (r.parentElement && r.parentElement.isContentEditable) r = r.parentElement;
      return { kind: 'ce', el: r };
    }
    return null;
  };

  // ---------------------------------------------------------------- interfaccia (shadow DOM isolato)
  let ui = null;
  let field = null;          // campo con il pulsante
  let job = null;            // correzione in corso / mostrata
  let posTimer = null;

  const ICON = '<svg viewBox="0 0 24 24" width="15" height="15" aria-hidden="true"><path d="M5 12.5l4.2 4.2L19 7" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"/></svg>';
  const ICON_REPLY = '<svg viewBox="0 0 24 24" width="15" height="15" aria-hidden="true"><path d="M4 5.5h16v10H9l-5 4z" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linejoin="round"/><path d="M8.5 10.5h.01M12 10.5h.01M15.5 10.5h.01" stroke="currentColor" stroke-width="2.6" stroke-linecap="round"/></svg>';

  const CSS = `
  :host { all: initial; }
  * { box-sizing: border-box; font-family: "Segoe UI", system-ui, -apple-system, Roboto, Arial, sans-serif; }
  .btn { position: fixed; width: 26px; height: 26px; border-radius: 50%; border: 2px solid #fff; padding: 0;
    background: #2563eb; color: #fff; display: none; align-items: center; justify-content: center; cursor: pointer;
    box-shadow: 0 2px 8px rgba(0,0,0,.25); opacity: .9; transition: transform .12s, opacity .12s; }
  .btn:hover { transform: scale(1.1); opacity: 1; }
  .btn.busy { animation: spin 1s linear infinite; background: #64748b; }
  .btn.reply { background: #7c3aed; }
  .ghost { position: fixed; display: none; pointer-events: none; white-space: pre; overflow: hidden; text-overflow: ellipsis;
    color: #94a3b8; }
  .ghost .k { display: inline-block; margin-left: 6px; padding: 0 5px; border: 1px solid #cbd5e1; border-radius: 4px;
    font: 600 10px/15px "Segoe UI", system-ui, sans-serif; color: #64748b; background: #f8fafc; vertical-align: 1px; }
  @keyframes spin { to { transform: rotate(360deg); } }
  .panel { position: fixed; width: min(460px, calc(100vw - 16px)); max-height: min(70vh, 520px); display: none; flex-direction: column;
    background: #fff; color: #0f172a; border: 1px solid #e2e8f0; border-radius: 12px; box-shadow: 0 12px 40px rgba(15,23,42,.25); overflow: hidden; font-size: 14px; }
  .head { display: flex; align-items: center; gap: 8px; padding: 10px 12px; border-bottom: 1px solid #e2e8f0; font-weight: 600; }
  .dot { width: 18px; height: 18px; border-radius: 50%; background: #2563eb; color: #fff; display: inline-flex; align-items: center; justify-content: center; }
  .head .sp { flex: 1; }
  .x { border: 0; background: transparent; font-size: 18px; line-height: 1; cursor: pointer; color: #64748b; padding: 2px 6px; border-radius: 6px; }
  .x:hover { background: #f1f5f9; }
  .body { padding: 12px; overflow: auto; line-height: 1.55; white-space: pre-wrap; word-break: break-word; }
  .msg { color: #475569; }
  .err { color: #b91c1c; }
  .ok { color: #15803d; font-weight: 600; }
  del { background: #fee2e2; color: #b91c1c; text-decoration: line-through; border-radius: 3px; }
  ins { background: #dcfce7; color: #166534; text-decoration: none; border-radius: 3px; font-weight: 600; }
  .foot { display: flex; gap: 8px; align-items: center; padding: 10px 12px; border-top: 1px solid #e2e8f0; background: #f8fafc; }
  .foot .info { flex: 1; font-size: 12px; color: #64748b; }
  .b { border: 1px solid #cbd5e1; background: #fff; color: #0f172a; border-radius: 8px; padding: 6px 12px; cursor: pointer; font-size: 13px; }
  .b:hover { background: #f1f5f9; }
  .b.primary { background: #2563eb; border-color: #2563eb; color: #fff; }
  .b.primary:hover { background: #1d4ed8; }
  .spinner { display: inline-block; width: 14px; height: 14px; border: 2px solid #cbd5e1; border-top-color: #2563eb; border-radius: 50%; animation: spin .8s linear infinite; vertical-align: -2px; margin-right: 6px; }
  @media (prefers-color-scheme: dark) {
    .panel { background: #111827; color: #e5e7eb; border-color: #374151; }
    .head, .foot { border-color: #374151; } .foot { background: #0b1220; }
    .msg { color: #9ca3af; } .x:hover, .b:hover { background: #1f2937; }
    .b { background: #111827; color: #e5e7eb; border-color: #4b5563; }
    del { background: #4c1d1d; color: #fca5a5; } ins { background: #14321f; color: #86efac; }
  }`;

  const ensureUi = () => {
    if (ui && ui.root.isConnected) return ui;
    const root = document.createElement('cit-root');
    root.style.cssText = 'all:initial;position:fixed;top:0;left:0;width:0;height:0;z-index:2147483647;';
    const sr = root.attachShadow({ mode: 'closed' });
    sr.innerHTML = `<style>${CSS}</style>
      <button class="btn fix" type="button" title="Correggi in italiano (Correttore IT)">${ICON}</button>
      <button class="btn reply" type="button" title="Suggerisci la risposta (AI locale)">${ICON_REPLY}</button>
      <div class="ghost"><span class="g"></span><span class="k">Tab</span></div>
      <div class="panel" role="dialog" aria-label="Correttore IT">
        <div class="head"><span class="dot">${ICON}</span><span class="ttl">Correttore IT</span><span class="sp"></span><button class="x" type="button" title="Chiudi">×</button></div>
        <div class="body"></div>
        <div class="foot"><span class="info"></span>
          <button class="b again" type="button">Un'altra</button>
          <button class="b copy" type="button">Copia</button>
          <button class="b primary apply" type="button">Applica</button>
        </div>
      </div>`;
    (document.body || document.documentElement).appendChild(root);
    ui = {
      root, sr,
      btn: sr.querySelector('.btn.fix'),
      rbtn: sr.querySelector('.btn.reply'),
      ghost: sr.querySelector('.ghost'),
      ghostText: sr.querySelector('.ghost .g'),
      panel: sr.querySelector('.panel'),
      title: sr.querySelector('.ttl'),
      body: sr.querySelector('.body'),
      info: sr.querySelector('.info'),
      again: sr.querySelector('.again'),
      copy: sr.querySelector('.copy'),
      apply: sr.querySelector('.apply'),
      close: sr.querySelector('.x')
    };
    // mousedown + preventDefault: il campo non perde il focus né la selezione
    ui.btn.addEventListener('mousedown', (e) => { e.preventDefault(); e.stopPropagation(); onButton(); });
    ui.rbtn.addEventListener('mousedown', (e) => { e.preventDefault(); e.stopPropagation(); startReply(0.4); });
    ui.again.addEventListener('mousedown', (e) => e.preventDefault());
    ui.again.addEventListener('click', () => {
      if (job && job.kind === 'reply' && job.mode && job.mode !== 'rispondi') startTransform(job.mode, 0.85, job.target);
      else startReply(0.85);
    });
    ui.close.addEventListener('click', closePanel);
    ui.copy.addEventListener('click', onCopy);
    ui.apply.addEventListener('mousedown', (e) => e.preventDefault());
    ui.apply.addEventListener('click', onApply);
    ui.panel.addEventListener('keydown', (e) => { if (e.key === 'Escape') closePanel(); });
    return ui;
  };

  const hideButton = () => {
    if (ui) { ui.btn.style.display = 'none'; ui.rbtn.style.display = 'none'; }
    hideGhost();
    if (posTimer) { clearInterval(posTimer); posTimer = null; }
  };

  const placeButton = () => {
    if (!field || !ui) return;
    if (!field.el.isConnected) { hideButton(); field = null; return; }
    const r = field.el.getBoundingClientRect();
    if (r.width < 60 || r.height < 18 || r.bottom < 0 || r.top > innerHeight || r.right < 0 || r.left > innerWidth) {
      ui.btn.style.display = 'none';
      ui.rbtn.style.display = 'none';
      return;
    }
    const size = 26;
    const x = Math.min(innerWidth - size - 4, r.right - size - 6);
    const y = Math.min(innerHeight - size - 4, Math.max(4, r.bottom - size - 6));
    const top = (r.height < 40 ? Math.max(4, r.top + (r.height - size) / 2) : y) + 'px';
    ui.btn.style.left = x + 'px';
    ui.btn.style.top = top;
    ui.btn.style.display = 'flex';
    // pulsante risposta accanto, solo nei campi abbastanza larghi
    if (r.width >= 140) {
      ui.rbtn.style.left = (x - size - 6) + 'px';
      ui.rbtn.style.top = top;
      ui.rbtn.style.display = 'flex';
    } else ui.rbtn.style.display = 'none';
  };

  const showButtonFor = (f) => {
    ensureUi();
    field = f;
    placeButton();
    if (!posTimer) posTimer = setInterval(placeButton, 400);
  };

  document.addEventListener('focusin', (e) => {
    if (!allowed) return;
    const t = (e.composedPath && e.composedPath()[0]) || e.target;
    const f = editableFrom(t);
    if (f) showButtonFor(f);
  }, true);

  document.addEventListener('focusout', () => {
    setTimeout(() => {
      if (job && job.open) return;
      const f = editableFrom(deepActive());
      if (!f || !field || f.el !== field.el) hideButton();
    }, 150);
  }, true);

  addEventListener('scroll', () => { placeButton(); hideGhost(); if (job && job.open) placePanel(); }, true);
  addEventListener('resize', () => { placeButton(); hideGhost(); if (job && job.open) placePanel(); });

  // ---------------------------------------------------------------- contesto della pagina (messaggio a cui si risponde)
  const fieldText = (f) => (f.kind === 'value' ? f.el.value || '' : f.el.innerText || f.el.textContent || '');

  // testo selezionato fuori dal campo, altrimenti il testo del contenitore più vicino (la chat, la mail...)
  const pageContext = (f, max) => {
    const sel = getSelection();
    if (sel && !sel.isCollapsed && sel.rangeCount && !f.el.contains(sel.anchorNode)) {
      const s = sel.toString().trim();
      if (s.length > 2) return s.slice(-max);
    }
    const own = fieldText(f).trim();
    let el = f.el.parentElement;
    let best = '';
    for (let i = 0; el && i < 14 && el !== document.documentElement; i++, el = el.parentElement) {
      let t = (el.innerText || '');
      if (own) t = t.split(own).join(' ');
      t = t.replace(/[ \t ]+/g, ' ').replace(/\n\s*\n+/g, '\n').trim();
      if (t.length > best.length) best = t;
      if (t.length >= 600 || el === document.body) break;
    }
    return best.slice(-max);
  };

  let ctxCache = { el: null, at: 0, text: '' };
  const cachedContext = (f) => {
    const now = Date.now();
    if (ctxCache.el !== f.el || now - ctxCache.at > 15000) ctxCache = { el: f.el, at: now, text: pageContext(f, 800) };
    return ctxCache.text;
  };

  // ---------------------------------------------------------------- completamento con Tab (testo grigio)
  let ghost = null;          // { el, before, text }
  let sugTimer = null;
  let sugSeq = 0;

  const hideGhost = () => {
    ghost = null;
    if (ui) ui.ghost.style.display = 'none';
  };

  // testo prima del cursore; null se c'è una selezione o se dopo il cursore, sulla stessa riga, c'è altro testo
  const beforeCaret = (f) => {
    const el = f.el;
    if (f.kind === 'value') {
      const s = el.selectionStart, e = el.selectionEnd;
      if (s == null || s !== e) return null;
      const after = (el.value || '').slice(s);
      if (after && after[0] !== '\n') return null;
      return el.value.slice(0, s);
    }
    const root = el.getRootNode && el.getRootNode().getSelection ? el.getRootNode() : document;
    const sel = root.getSelection ? root.getSelection() : getSelection();
    if (!sel || !sel.rangeCount || !sel.isCollapsed || !el.contains(sel.anchorNode)) return null;
    const r = sel.getRangeAt(0);
    const post = document.createRange();
    post.selectNodeContents(el);
    post.setStart(r.endContainer, r.endOffset);
    const after = post.toString();
    if (after.trim() && !/^\s*\n/.test(after)) return null;
    const pre = document.createRange();
    pre.selectNodeContents(el);
    pre.setEnd(r.startContainer, r.startOffset);
    return pre.toString();
  };

  const MIRROR = ['boxSizing', 'width', 'borderTopWidth', 'borderRightWidth', 'borderBottomWidth', 'borderLeftWidth',
    'paddingTop', 'paddingRight', 'paddingBottom', 'paddingLeft', 'fontStyle', 'fontVariant', 'fontWeight', 'fontStretch',
    'fontSize', 'lineHeight', 'fontFamily', 'textAlign', 'textTransform', 'textIndent', 'letterSpacing', 'wordSpacing', 'tabSize'];

  // posizione del cursore sullo schermo
  const caretRect = (f) => {
    const el = f.el;
    if (f.kind === 'value') {
      const cs = getComputedStyle(el);
      const div = document.createElement('div');
      for (const p of MIRROR) div.style[p] = cs[p];
      div.style.position = 'absolute';
      div.style.visibility = 'hidden';
      div.style.top = '0';
      div.style.left = '-9999px';
      div.style.whiteSpace = el.tagName === 'INPUT' ? 'pre' : 'pre-wrap';
      div.style.overflowWrap = 'break-word';
      div.textContent = el.value.slice(0, el.selectionEnd);
      const span = document.createElement('span');
      span.textContent = '​';
      div.append(span);
      document.body.append(div);
      const r = el.getBoundingClientRect();
      const x = r.left + span.offsetLeft - el.scrollLeft + parseFloat(cs.borderLeftWidth || 0);
      const y = r.top + span.offsetTop - el.scrollTop + parseFloat(cs.borderTopWidth || 0);
      const h = span.offsetHeight || parseFloat(cs.fontSize) * 1.3;
      div.remove();
      return { left: x, top: y, height: h, right: r.right, start: r.left + parseFloat(cs.borderLeftWidth || 0) + parseFloat(cs.paddingLeft || 0) };
    }
    const sel = getSelection();
    if (!sel.rangeCount) return null;
    const r = sel.getRangeAt(0).cloneRange();
    let rect = r.getClientRects()[0];
    if (!rect || (!rect.height && !rect.left)) {
      const n = r.startContainer.nodeType === 1 ? r.startContainer : r.startContainer.parentElement;
      rect = n ? n.getBoundingClientRect() : null;
      if (!rect) return null;
      const fr = el.getBoundingClientRect();
      return { left: rect.left + 2, top: rect.top, height: Math.min(rect.height, 24), right: fr.right, start: fr.left + 8 };
    }
    const fr = el.getBoundingClientRect();
    return { left: rect.left, top: rect.top, height: rect.height, right: fr.right, start: fr.left + 8 };
  };

  const showGhost = (f, before, text) => {
    const cr = caretRect(f);
    if (!cr || !ui) return;
    const cs = getComputedStyle(f.kind === 'value' ? f.el : (getSelection().anchorNode && getSelection().anchorNode.parentElement) || f.el);
    const g = ui.ghost;
    ui.ghostText.textContent = text;
    g.style.fontFamily = cs.fontFamily;
    g.style.fontSize = cs.fontSize;
    g.style.fontWeight = cs.fontWeight;
    g.style.lineHeight = cr.height + 'px';
    let left = cr.left, top = cr.top;
    let room = cr.right - left - 8;
    if (room < 90) { left = Math.max(4, cr.start); top = cr.top + cr.height; room = cr.right - left - 8; }   // poco spazio: a capo, sotto il cursore
    g.style.left = left + 'px';
    g.style.top = top + 'px';
    g.style.maxWidth = Math.max(90, room) + 'px';
    g.style.display = 'block';
    ghost = { el: f.el, before, text };
  };

  const requestSuggestion = () => {
    sugTimer = null;
    if (!allowed || !settings.suggest || !field || (job && job.open)) return;
    const f = field;
    const before = beforeCaret(f);
    if (before == null || before.trim().length < 4 || /\n\s*$/.test(before)) return;
    const my = ++sugSeq;
    try {
      chrome.runtime.sendMessage({ type: 'cit-suggest', before, context: cachedContext(f) }, (res) => {
        void chrome.runtime.lastError;
        if (my !== sugSeq || !res || !res.ok || !res.data || !res.data.text) return;
        if (field !== f || beforeCaret(f) !== before) return;   // nel frattempo l'utente ha scritto altro
        showGhost(f, before, res.data.text);
      });
    } catch (e) { }
  };

  const scheduleSuggestion = () => {
    hideGhost();
    sugSeq++;
    if (sugTimer) clearTimeout(sugTimer);
    if (!allowed || !settings.suggest) return;
    sugTimer = setTimeout(requestSuggestion, 150);
  };

  document.addEventListener('input', (e) => {
    if (!field || e.isComposing) return;
    const t = (e.composedPath && e.composedPath()[0]) || e.target;
    if (t !== field.el && !(field.el.contains && field.el.contains(t))) return;
    scheduleSuggestion();
  }, true);

  const acceptGhost = () => {
    const g = ghost;
    hideGhost();
    if (!g || !field || field.el !== g.el || beforeCaret(field) !== g.before) return false;
    const el = field.el;
    let done = false;
    try { done = document.execCommand('insertText', false, g.text); } catch (e) { done = false; }
    if (!done && field.kind === 'value') {
      const s = el.selectionStart;
      const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
      Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, el.value.slice(0, s) + g.text + el.value.slice(s));
      el.setSelectionRange(s + g.text.length, s + g.text.length);
      el.dispatchEvent(new Event('input', { bubbles: true }));
      done = true;
    }
    return done;
  };

  // Tab accetta il suggerimento, Esc lo nasconde; qualsiasi altro tasto lo toglie (ne arriva uno nuovo)
  addEventListener('keydown', (e) => {
    if (!ghost) return;
    if (e.key === 'Tab' && !e.shiftKey && !e.ctrlKey && !e.altKey && !e.metaKey) {
      e.preventDefault();
      e.stopImmediatePropagation();
      acceptGhost();
      return;
    }
    if (['Shift', 'Control', 'Alt', 'Meta', 'CapsLock'].includes(e.key)) return;
    hideGhost();
  }, true);
  document.addEventListener('mousedown', (e) => { if (ghost && !(ui && e.composedPath().includes(ui.root))) hideGhost(); }, true);

  // ---------------------------------------------------------------- risposta suggerita
  let replyPort = null;

  const stopReply = () => {
    if (replyPort) { try { replyPort.disconnect(); } catch (e) { } replyPort = null; }
  };

  const MODE_TITLES = {
    rispondi: 'Risposta suggerita', completa: 'Completa la frase', formale: 'Più formale',
    semplice: 'Più semplice e breve', traduci: 'Traduzione'
  };

  // come nel programma Windows: la continuazione si attacca al testo con lo spazio giusto
  const joinContinuation = (text, cont) => {
    cont = (cont || '').replace(/\s+$/, '');
    const t = text.replace(/\s+$/, '');
    if (t && cont.trimStart().startsWith(t)) cont = cont.trimStart().slice(t.length);
    if (!cont.trim()) return text;
    if (!text) return cont.trim();
    if (/\s$/.test(text)) return text + cont.trimStart();
    if (/^\s/.test(cont)) return text + ' ' + cont.trimStart();
    if (/^[.,;:!?)…]/.test(cont)) return text + cont;
    return text + ' ' + cont;
  };

  const startReply = (temperature) => startTransform('rispondi', temperature, null);

  // rispondi, completa, formale, semplice, traduci: il testo arriva a pezzi mentre il motore lo scrive
  const startTransform = (mode, temperature, given) => {
    if (!given && !field) return;
    if (job && job.busy && job.kind !== 'reply') return;
    stopReply();
    hideGhost();
    ensureUi();
    let target = given;
    if (!target || mode === 'rispondi') {
      const f = field;
      if (f.kind === 'value') { const v = f.el.value || ''; target = { f, text: v, start: 0, end: v.length, whole: true }; }
      else { const range = document.createRange(); range.selectNodeContents(f.el); target = { f, text: fieldText(f), range, whole: true }; }
    }
    const f = target.f;
    const draft = target.text || '';
    if (mode !== 'rispondi' && !draft.trim()) {
      job = { kind: 'reply', mode, target, original: '', corrected: '', busy: false, open: true };
      showMessage('Il campo è vuoto: scrivi o seleziona del testo.', 'msg');
      return;
    }
    const context = f ? pageContext(f, 3000) : '';
    job = { kind: 'reply', mode, target, original: draft, corrected: '', raw: '', busy: true, open: true, t0: Date.now(), temperature };
    const my = job;
    ui.title.textContent = MODE_TITLES[mode] || 'Correttore IT';
    ui.body.textContent = '';
    const s = document.createElement('span'); s.className = 'spinner';
    const m = document.createElement('span'); m.className = 'msg';
    m.textContent = mode === 'rispondi' ? (draft.trim() ? 'Completo la tua risposta…' : 'Scrivo una risposta…') : 'Scrivo…';
    ui.body.append(s, m);
    setFoot('100% locale: il testo non esce dal PC', false, false);
    openPanel();
    let port;
    try { port = chrome.runtime.connect({ name: 'cit-reply' }); }
    catch (e) { my.busy = false; showMessage('Estensione aggiornata: ricarica la pagina (F5).', 'err'); return; }
    replyPort = port;
    port.onMessage.addListener((msg) => {
      if (job !== my) { try { port.disconnect(); } catch (e) { } return; }
      if (msg.chunk) {
        my.raw += msg.chunk;
        my.corrected = mode === 'completa' ? joinContinuation(draft, my.raw) : my.raw;
        ui.body.textContent = my.corrected;
        placePanel();
      } else if (msg.error) {
        my.busy = false;
        showMessage(msg.error, 'err');
      } else if (msg.done) {
        my.busy = false;
        replyPort = null;
        my.corrected = mode === 'completa' ? joinContinuation(draft, my.raw) : my.raw.trim();
        if (!my.corrected.trim()) { showMessage('Nessun testo: riprova.', 'err'); return; }
        ui.body.textContent = my.corrected;
        const canApply = !!(f && f.el && f.el.isConnected);
        setFoot(`Tab o Inserisci · ${((Date.now() - my.t0) / 1000).toFixed(1)} s`, true, canApply, true);
        ui.apply.textContent = 'Inserisci';
        openPanel();
      }
    });
    port.onDisconnect.addListener(() => {
      void chrome.runtime.lastError;
      if (job === my && my.busy) { my.busy = false; showMessage('Collegamento interrotto: riprova.', 'err'); }
    });
    port.postMessage({ mode, text: draft, draft, context, temperature });
  };

  // con il pannello della risposta aperto, Tab = Inserisci
  addEventListener('keydown', (e) => {
    if (e.key !== 'Tab' || e.shiftKey || e.ctrlKey || e.altKey || e.metaKey) return;
    if (!job || job.kind !== 'reply' || job.busy || !job.corrected || !ui || ui.apply.style.display === 'none') return;
    e.preventDefault();
    e.stopImmediatePropagation();
    onApply();
  }, true);

  // ---------------------------------------------------------------- testo da correggere
  const capture = (f) => {
    const el = f.el;
    if (f.kind === 'value') {
      const v = el.value || '';
      let s = el.selectionStart, e = el.selectionEnd;
      if (s == null || e == null || s === e) { s = 0; e = v.length; }
      return { f, text: v.slice(s, e), start: s, end: e, whole: s === 0 && e === v.length };
    }
    const sel = (el.getRootNode && el.getRootNode().getSelection) ? el.getRootNode().getSelection() : getSelection();
    if (sel && sel.rangeCount && !sel.isCollapsed && el.contains(sel.anchorNode) && el.contains(sel.focusNode)) {
      const range = sel.getRangeAt(0).cloneRange();
      return { f, text: sel.toString(), range, rangeText: range.toString(), whole: false };
    }
    const range = document.createRange();
    range.selectNodeContents(el);
    return { f, text: el.innerText || el.textContent || '', range, whole: true };
  };

  const onButton = () => {
    if (!field) return;
    if (job && job.busy) return;
    start(capture(field));
  };

  const start = (target) => {
    ensureUi();
    stopReply();
    hideGhost();
    ui.title.textContent = 'Correttore IT';
    const text = target.text || '';
    job = { kind: 'fix', target, original: text, corrected: null, busy: true, open: true };
    if (!text.trim()) { showMessage('Il campo è vuoto: scrivi o seleziona del testo.', 'msg'); job.busy = false; return; }
    if (text.length > MAX_CHARS) { showMessage('Testo troppo lungo (massimo 20.000 caratteri): selezionane una parte.', 'err'); job.busy = false; return; }
    ui.btn.classList.add('busy');
    showLoading();
    const my = job;
    let sent = false;
    try {
      chrome.runtime.sendMessage({ type: 'cit-correct', text }, (res) => {
        if (job !== my) return;
        my.busy = false;
        ui.btn.classList.remove('busy');
        if (chrome.runtime.lastError || !res) { showMessage('Estensione aggiornata: ricarica la pagina (F5).', 'err'); return; }
        if (!res.ok) { showMessage(res.error || 'Errore sconosciuto', 'err'); return; }
        my.corrected = res.data.corrected;
        my.ms = res.data.ms;
        showResult();
      });
      sent = true;
    } catch (e) { }
    if (!sent) { my.busy = false; ui.btn.classList.remove('busy'); showMessage('Estensione aggiornata: ricarica la pagina (F5).', 'err'); }
  };

  // ---------------------------------------------------------------- pannello
  const anchorRect = () => {
    if (!job) return null;
    const t = job.target;
    if (t.f && t.f.el && t.f.el.isConnected) return t.f.el.getBoundingClientRect();
    if (t.rect) return t.rect;
    return null;
  };

  const placePanel = () => {
    if (!ui) return;
    const p = ui.panel;
    const w = Math.min(460, innerWidth - 16);
    const h = p.offsetHeight || 200;
    const r = anchorRect() || { left: innerWidth - w - 16, right: innerWidth - 16, top: 16, bottom: 16 };
    let x = Math.min(Math.max(8, r.right - w), innerWidth - w - 8);
    let y = r.bottom + 8;
    if (y + h > innerHeight - 8) y = r.top - h - 8;
    if (y < 8) y = Math.max(8, innerHeight - h - 8);
    p.style.left = x + 'px';
    p.style.top = y + 'px';
  };

  const openPanel = () => {
    ui.panel.style.display = 'flex';
    placePanel();
    requestAnimationFrame(placePanel);
  };

  const closePanel = () => {
    stopReply();
    if (ui) { ui.panel.style.display = 'none'; ui.btn.classList.remove('busy'); }
    job = null;
  };

  const setFoot = (info, canCopy, canApply, canAgain) => {
    ui.info.textContent = info || '';
    ui.again.style.display = canAgain ? '' : 'none';
    ui.copy.style.display = canCopy ? '' : 'none';
    ui.apply.style.display = canApply ? '' : 'none';
    ui.apply.textContent = 'Applica';
  };

  const showLoading = () => {
    ui.body.textContent = '';
    const s = document.createElement('span'); s.className = 'spinner';
    const m = document.createElement('span'); m.className = 'msg';
    m.textContent = 'Correzione in corso sul tuo PC… (la prima volta il motore impiega qualche secondo ad avviarsi)';
    ui.body.append(s, m);
    setFoot('100% locale: il testo non esce dal PC', false, false);
    openPanel();
  };

  const showMessage = (text, cls) => {
    ui.body.textContent = '';
    const m = document.createElement('div'); m.className = cls; m.textContent = text;
    ui.body.append(m);
    setFoot('', false, false);
    openPanel();
  };

  const showResult = () => {
    const { original, corrected } = job;
    ui.body.textContent = '';
    const edits = diffOps(original, corrected);
    const changes = edits.filter((o) => o.t !== '=').length;
    if (!changes) {
      const m = document.createElement('div'); m.className = 'ok'; m.textContent = '✓ Nessun errore trovato';
      ui.body.append(m);
      setFoot(job.ms ? `Controllato in ${(job.ms / 1000).toFixed(1)} s` : '', false, false);
      openPanel();
      return;
    }
    for (const o of edits) {
      if (o.t === '=') ui.body.append(document.createTextNode(o.v));
      else { const n = document.createElement(o.t === '-' ? 'del' : 'ins'); n.textContent = o.v; ui.body.append(n); }
    }
    const canApply = !!(job.target.f && job.target.f.el && job.target.f.el.isConnected);
    const nEdits = groupEdits(edits).length;
    setFoot(`${nEdits} ${nEdits === 1 ? 'correzione' : 'correzioni'}${job.ms ? ' · ' + (job.ms / 1000).toFixed(1) + ' s' : ''}`, true, canApply);
    openPanel();
    // niente focus su "Applica": il campo deve tenere il focus, altrimenti l'editor sposta il cursore
  };

  const onCopy = async () => {
    if (!job || job.corrected == null) return;
    try { await navigator.clipboard.writeText(job.corrected); ui.info.textContent = 'Copiato negli appunti ✓'; }
    catch (e) { ui.info.textContent = 'Copia non riuscita: seleziona il testo e usa Ctrl+C'; }
  };

  // ---------------------------------------------------------------- applicazione nel campo
  const norm = (x) => String(x || '').replace(/[\s ]+/g, ' ').trim();

  const onApply = async () => {
    if (!job || job.corrected == null || job.applying) return;
    const my = job;
    const t = my.target;
    my.applying = true;
    ui.info.textContent = 'Applico…';
    let ok = false;
    try {
      if (t.f.kind === 'value') ok = applyValue(t, my.corrected);
      else if (!norm(my.original)) ok = await insertIntoEmpty(t.f.el, my.corrected);
      else ok = await applyCE(t, my.original, my.corrected);
    } catch (e) { ok = false; }
    my.applying = false;
    if (job !== my) return;
    // controllo finale: il campo deve contenere davvero il testo corretto
    let check = true;
    if (ok && t.whole && t.f.el.isConnected) {
      await new Promise((r) => setTimeout(r, 60));
      const now = t.f.kind === 'value' ? t.f.el.value : (t.f.el.innerText || t.f.el.textContent);
      check = norm(now) === norm(my.corrected);
    }
    if (ok && check) {
      showMessage(my.kind === 'reply' ? '✓ Fatto  (Ctrl+Z per annullare)' : '✓ Correzione applicata  (Ctrl+Z per annullare)', 'ok');
      setTimeout(() => { if (job === my) closePanel(); }, 1400);
    } else if (ok) {
      showMessage('Correzione applicata, ma l’editor di questo sito ha modificato l’inserimento: controlla il testo (Ctrl+Z annulla) oppure usa Copia.', 'err');
      setFoot('', true, false);
    } else {
      ui.info.textContent = 'Non riesco ad applicare in questo editor: usa Copia e incolla con Ctrl+V.';
    }
  };

  const applyValue = (t, corrected) => {
    const el = t.f.el;
    const v = el.value || '';
    let s = t.start, e = t.end;
    if (v.slice(s, e) !== t.text) {
      const i = v.indexOf(t.text);
      if (i < 0) return false;
      s = i; e = i + t.text.length;
    }
    el.focus();
    el.setSelectionRange(s, e);
    let done = false;
    try { done = document.execCommand('insertText', false, corrected); } catch (err) { done = false; }
    if (!done || (el.value || '').slice(s, s + corrected.length) !== corrected) {
      const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
      const setter = Object.getOwnPropertyDescriptor(proto, 'value').set;
      setter.call(el, v.slice(0, s) + corrected + v.slice(e));
      el.dispatchEvent(new Event('input', { bubbles: true }));
      el.dispatchEvent(new Event('change', { bubbles: true }));
      el.setSelectionRange(s + corrected.length, s + corrected.length);
    }
    return true;
  };

  // gli editor dei siti (WhatsApp, Gmail, Facebook...) leggono la selezione solo all'evento
  // "selectionchange", che arriva dopo: senza attesa inseriscono al vecchio cursore
  const settle = () => new Promise((r) => requestAnimationFrame(() => setTimeout(r, 30)));

  const selectRange = async (el, range, expected) => {
    const act = deepActive();
    if (!act || !el.contains(act)) { el.focus({ preventScroll: true }); await settle(); }
    const sel = getSelection();
    sel.removeAllRanges();
    sel.addRange(range);
    await settle();
    const now = getSelection();
    return !!(now.rangeCount && now.getRangeAt(0).toString() === expected);
  };

  const insertPlain = (text) => {
    const lines = text.split('\n');
    let ok = true;
    lines.forEach((line, i) => {
      if (i > 0) ok = document.execCommand('insertLineBreak') && ok;
      if (line) ok = document.execCommand('insertText', false, line) && ok;
    });
    return ok;
  };

  // testo del campo come sequenza di nodi di testo (stesso conteggio di Range.toString)
  const textMap = (el) => {
    const nodes = [];
    let full = '';
    const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
    for (let n = walker.nextNode(); n; n = walker.nextNode()) { nodes.push({ n, at: full.length }); full += n.nodeValue; }
    // posizione -> { nodo, offset }; "end" preferisce la fine del nodo precedente
    const point = (pos, end) => {
      for (const x of nodes) {
        const len = x.n.nodeValue.length;
        if (end ? pos > x.at && pos <= x.at + len : pos >= x.at && pos < x.at + len) return { n: x.n, o: pos - x.at };
      }
      const last = nodes[nodes.length - 1];
      return last && pos === full.length ? { n: last.n, o: last.n.nodeValue.length } : null;
    };
    const rangeFor = (idx, len) => {
      const a = point(idx, false), b = point(idx + len, true);
      if (!a || !b) return null;
      const r = document.createRange();
      r.setStart(a.n, a.o);
      r.setEnd(b.n, b.o);
      return r;
    };
    return { full, rangeFor };
  };

  const tick = () => new Promise((r) => setTimeout(r, 25));
  const squash = (x) => String(x || '').replace(/[\s ​]+/g, '');

  // sostituisce il testo "del" alla posizione idx con "ins" e controlla che sia stato davvero
  // sostituito (e non aggiunto accanto al cursore); se no annulla e riprova una volta
  const replaceAt = async (el, idx, del, ins) => {
    for (let attempt = 0; attempt < 2; attempt++) {
      const map = textMap(el);
      if (map.full.substr(idx, del.length) !== del) return false;
      const expected = squash(map.full.slice(0, idx) + ins + map.full.slice(idx + del.length));
      const r = map.rangeFor(idx, del.length);
      if (!r) return false;
      if (!(await selectRange(el, r, del))) { await settle(); continue; }
      let done = false;
      try { done = insertPlain(ins); } catch (e) { done = false; }
      if (!done) {
        r.deleteContents();
        r.insertNode(document.createTextNode(ins));
        el.dispatchEvent(new Event('input', { bubbles: true }));
      }
      await tick();   // si lascia al sito il tempo di aggiornare il testo
      if (squash(textMap(el).full) === expected) return true;
      if (done) { try { document.execCommand('undo'); } catch (e) { } await settle(); }
    }
    return false;
  };

  // contenteditable: si sostituisce una parola alla volta, rileggendo il testo dopo ogni modifica
  // (molti siti ricostruiscono il testo dopo ogni inserimento). Grassetti e link restano intatti.
  const applyCE = async (t, original, corrected) => {
    const el = t.f.el;
    if (!el.isConnected) return false;
    if (!t.whole) {
      // selezione parziale: si ritrova la posizione del testo selezionato
      const map = textMap(el);
      const del = t.rangeText;
      let idx = -1;
      if (t.range && el.contains(t.range.startContainer) && t.range.toString() === del) {
        const pre = document.createRange();
        pre.selectNodeContents(el);
        pre.setEnd(t.range.startContainer, t.range.startOffset);
        idx = pre.toString().length;
      }
      if (idx < 0 || map.full.substr(idx, del.length) !== del) idx = map.full.indexOf(del);
      if (idx < 0 || !del) return false;
      return replaceAt(el, idx, del, corrected);
    }
    const edits = groupEdits(diffOps(original, corrected));
    let cursor = 0;
    let applied = 0;
    for (const ed of edits) {
      const fail = () => (applied ? true : applyWhole(el, corrected));   // se qualcosa è già stato applicato, decide il controllo finale
      if (!ed.del) return fail();
      const idx = textMap(el).full.indexOf(ed.del, cursor);
      if (idx < 0) return fail();
      if (!(await replaceAt(el, idx, ed.del, ed.ins))) return fail();
      applied++;
      cursor = idx + ed.ins.length;
    }
    return true;
  };

  // campo vuoto (contenteditable): si scrive alla fine del campo
  const insertIntoEmpty = async (el, text) => {
    if (!el.isConnected) return false;
    el.focus({ preventScroll: true });
    await settle();
    const r = document.createRange();
    r.selectNodeContents(el);
    r.collapse(false);
    const sel = getSelection();
    sel.removeAllRanges();
    sel.addRange(r);
    await settle();
    let done = false;
    try { done = insertPlain(text); } catch (e) { done = false; }
    if (!done) {
      r.insertNode(document.createTextNode(text));
      el.dispatchEvent(new Event('input', { bubbles: true }));
    }
    await tick();
    return true;
  };

  const applyWhole = (el, corrected) => {
    const full = textMap(el).full;
    return full ? replaceAt(el, 0, full, corrected) : false;
  };

  // ---------------------------------------------------------------- confronto parola per parola
  const tokenize = (s) => s.match(/\s+|[\p{L}\p{N}_]+(?:['’][\p{L}\p{N}_]+)*['’]?|[^\s\p{L}\p{N}_]/gu) || [];

  const diffOps = (a, b) => {
    const A = tokenize(a), B = tokenize(b);
    let p = 0;
    while (p < A.length && p < B.length && A[p] === B[p]) p++;
    let sa = A.length, sb = B.length;
    while (sa > p && sb > p && A[sa - 1] === B[sb - 1]) { sa--; sb--; }
    const out = [];
    const push = (t, v) => { if (!v) return; const l = out[out.length - 1]; if (l && l.t === t) l.v += v; else out.push({ t, v }); };
    push('=', A.slice(0, p).join(''));
    const MA = A.slice(p, sa), MB = B.slice(p, sb);
    if (MA.length * MB.length > 2500000) {
      push('-', MA.join('')); push('+', MB.join(''));
    } else {
      const n = MA.length, m = MB.length, W = m + 1;
      const L = new Uint32Array((n + 1) * W);
      for (let i = n - 1; i >= 0; i--)
        for (let j = m - 1; j >= 0; j--)
          L[i * W + j] = MA[i] === MB[j] ? L[(i + 1) * W + j + 1] + 1 : Math.max(L[(i + 1) * W + j], L[i * W + j + 1]);
      let i = 0, j = 0;
      while (i < n && j < m) {
        if (MA[i] === MB[j]) { push('=', MA[i]); i++; j++; }
        else if (L[(i + 1) * W + j] >= L[i * W + j + 1]) { push('-', MA[i]); i++; }
        else { push('+', MB[j]); j++; }
      }
      while (i < n) push('-', MA[i++]);
      while (j < m) push('+', MB[j++]);
    }
    push('=', A.slice(sa).join(''));
    return out;
  };

  // gruppi { del, ins }: le sole inserzioni vengono agganciate alla parola vicina
  const groupEdits = (ops) => {
    const res = [];
    for (let i = 0; i < ops.length; i++) {
      if (ops[i].t === '=') continue;
      let del = '', ins = '';
      let j = i;
      while (j < ops.length && ops[j].t !== '=') { if (ops[j].t === '-') del += ops[j].v; else ins += ops[j].v; j++; }
      if (!del) {
        const prev = i > 0 ? ops[i - 1].v : '';
        const tok = (prev.match(/(\S+)$/) || [])[1];
        if (tok) { del = tok; ins = tok + ins; }
        else {
          const next = j < ops.length ? ops[j].v : '';
          const t2 = (next.match(/^(\S+)/) || [])[1];
          if (t2) { del = t2; ins = ins + t2; }
        }
      }
      res.push({ del, ins });
      i = j - 1;
    }
    return res;
  };

  // ---------------------------------------------------------------- menu del tasto destro
  try {
    chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
      if (!msg) return;
      if (msg.type === 'cit-host') { sendResponse({ host: HOST }); return; }
      if (msg.type !== 'cit-ctx') return;
      if (!settings.enabled) return;
      const mode = msg.mode || 'fix';
      const f = editableFrom(deepActive());
      let target;
      if (f) { field = f; target = capture(f); }
      else {
        // testo della pagina (non modificabile): si mostra il risultato, con Copia
        const sel = getSelection();
        const text = (sel && sel.toString()) || msg.selectionText || '';
        let rect = null;
        if (sel && sel.rangeCount) rect = sel.getRangeAt(0).getBoundingClientRect();
        target = { f: null, text, rect, whole: false };
      }
      if (mode === 'fix') { start(target); return; }
      if (mode === 'rispondi' && !f) {
        // risposta al testo selezionato sulla pagina: si scrive nel campo attivo, se c'è
        ensureUi();
        job = { kind: 'reply', mode, target, original: '', corrected: '', busy: false, open: true };
        showMessage('Clicca nel campo dove vuoi scrivere la risposta, seleziona il messaggio e premi il pulsante viola.', 'msg');
        return;
      }
      startTransform(mode, mode === 'rispondi' ? 0.4 : 0.2, target);
    });
  } catch (e) { }
})();
