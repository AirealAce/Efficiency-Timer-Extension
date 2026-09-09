'use strict';

(() => {
  if (globalThis.__reflectionTimerContentLoaded) {
    return;
  }
  globalThis.__reflectionTimerContentLoaded = true;

  const HOST_ID = '__aaron-reflection-timer-host-v2';
  const MAX_REFLECTION_LENGTH = 5000;
  let sfxVolume = 0.5;
  let activePromptIsTest = false;
  let promptVolumeSet = false;
  const log = (event, details = {}) => globalThis.TimerDiagnosticClient?.event(event, {
    hasPrompt: Boolean(document.getElementById(HOST_ID)), isTest: activePromptIsTest,
    visibility: document.hidden ? 'hidden' : 'visible', ...details
  });
  document.addEventListener?.('visibilitychange', () => log('page.visibility'));
  globalThis.addEventListener?.('pageshow', (event) => log('page.shown', { persisted: event.persisted }));
  globalThis.addEventListener?.('pagehide', (event) => log('page.hidden', { persisted: event.persisted }));

  chrome.storage.local.get(['sfxVolume']).then((result) => {
    const saved = Number(result.sfxVolume);
    if (Number.isFinite(saved) && !promptVolumeSet) {
      sfxVolume = Math.max(0, Math.min(1, saved / 100));
    }
  }).catch(() => {});

  function sendMessage(message) {
    const startedAt = Date.now();
    return new Promise((resolve, reject) => {
      try {
        chrome.runtime.sendMessage(message, (response) => {
          const lastError = chrome.runtime.lastError;
          if (lastError) {
            log('client.failure', { action: message.action, errorKind: globalThis.TimerDiagnosticClient?.errorKind(lastError) || 'unknown', elapsedMs: Date.now() - startedAt });
            reject(new Error(lastError.message));
          } else if (!response || response.success === false) {
            log('client.failure', { action: message.action, errorKind: 'rejected', elapsedMs: Date.now() - startedAt });
            const error = new Error((response && response.error) || 'The extension did not respond.');
            error.code = response && response.errorCode;
            reject(error);
          } else {
            resolve(response);
          }
        });
      } catch (error) {
        log('client.failure', { action: message.action, errorKind: globalThis.TimerDiagnosticClient?.errorKind(error) || 'unknown', elapsedMs: Date.now() - startedAt });
        reject(error);
      }
    });
  }

  async function playNotificationSound() {
    if (document.hidden || sfxVolume <= 0) {
      log('sound.result', { outcome: document.hidden ? 'hidden' : 'muted' });
      return;
    }
    try {
      const audio = new Audio(chrome.runtime.getURL('notification.wav'));
      audio.volume = sfxVolume;
      await audio.play();
      log('sound.result', { outcome: 'success' });
    } catch (_error) {
      log('sound.result', { outcome: 'blocked' });
      // Browsers may block autoplay; the visual prompt still appears.
    }
  }

  function formatDuration(totalSeconds) {
    const seconds = Math.max(0, Number.parseInt(totalSeconds, 10) || 0);
    const hours = Math.floor(seconds / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    if (hours > 0) {
      return `${hours}h ${minutes}m`;
    }
    if (minutes > 0) {
      return `${minutes} minute${minutes === 1 ? '' : 's'}`;
    }
    return `${seconds} second${seconds === 1 ? '' : 's'}`;
  }

  function dismissLocalPrompt() {
    document.getElementById(HOST_ID)?.remove();
  }

  function createElement(tagName, attributes = {}, text = '') {
    const element = document.createElement(tagName);
    Object.entries(attributes).forEach(([name, value]) => {
      if (name === 'className') {
        element.className = value;
      } else {
        element.setAttribute(name, value);
      }
    });
    if (text) {
      element.textContent = text;
    }
    return element;
  }

  function buildPrompt(options) {
    dismissLocalPrompt();
    activePromptIsTest = options.isTest;
    if (Number.isFinite(options.sfxVolume)) {
      sfxVolume = Math.max(0, Math.min(1, options.sfxVolume / 100));
      promptVolumeSet = true;
    }

    const host = createElement('div', { id: HOST_ID });
    host.style.setProperty('all', 'initial', 'important');
    host.style.setProperty('display', 'block', 'important');
    const shadow = host.attachShadow({ mode: 'closed' });
    const style = createElement('style');
    style.textContent = `
      :host { all: initial; }
      *, *::before, *::after { box-sizing: border-box; }
      .backdrop {
        position: fixed;
        inset: 0;
        z-index: 2147483646;
        background: rgba(15, 23, 42, 0.24);
        backdrop-filter: blur(2px);
      }
      .dialog {
        position: fixed;
        right: 24px;
        bottom: 24px;
        z-index: 2147483647;
        width: min(440px, calc(100vw - 32px));
        padding: 24px;
        border: 1px solid light-dark(#dbe3ec, #334155);
        border-radius: 20px;
        color: light-dark(#172033, #f8fafc);
        background: light-dark(rgba(255,255,255,.98), rgba(15,23,42,.98));
        box-shadow: 0 24px 70px rgba(15, 23, 42, .35);
        font: 15px/1.45 -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
        animation: reflection-enter .18s ease-out;
        color-scheme: light dark;
      }
      @keyframes reflection-enter {
        from { opacity: 0; transform: translateY(12px) scale(.98); }
        to { opacity: 1; transform: translateY(0) scale(1); }
      }
      .eyebrow {
        margin: 0 0 6px;
        color: #16a34a;
        font-size: 12px;
        font-weight: 750;
        letter-spacing: .08em;
        text-transform: uppercase;
      }
      h2 { margin: 0; font-size: 26px; line-height: 1.15; }
      .context { margin: 8px 0 18px; color: light-dark(#526078, #cbd5e1); }
      label { display: block; margin-bottom: 7px; font-weight: 650; }
      textarea {
        display: block;
        width: 100%;
        min-height: 112px;
        max-height: 40vh;
        padding: 13px 14px;
        resize: vertical;
        border: 1px solid light-dark(#cbd5e1, #475569);
        border-radius: 12px;
        outline: none;
        color: inherit;
        background: light-dark(#f8fafc, #1e293b);
        font: inherit;
      }
      textarea:focus { border-color: #16a34a; box-shadow: 0 0 0 3px rgba(22,163,74,.16); }
      .meta {
        min-height: 22px;
        margin-top: 7px;
        display: flex;
        justify-content: space-between;
        gap: 12px;
        color: light-dark(#64748b, #94a3b8);
        font-size: 12px;
      }
      .status.error { color: #dc2626; }
      .status.success { color: #16a34a; }
      .actions { display: flex; justify-content: flex-end; gap: 10px; margin-top: 14px; }
      button {
        min-height: 40px;
        padding: 8px 16px;
        border: 0;
        border-radius: 10px;
        cursor: pointer;
        font: 650 14px/1 -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
      }
      button:focus-visible { outline: 3px solid rgba(22,163,74,.28); outline-offset: 2px; }
      button:disabled { cursor: wait; opacity: .65; }
      button[hidden] { display: none; }
      .skip { color: inherit; background: light-dark(#e2e8f0, #334155); }
      .settings { color: inherit; background: light-dark(#e2e8f0, #334155); }
      .submit { color: white; background: #16a34a; }
      @media (max-width: 520px) {
        .dialog { right: 16px; bottom: 16px; padding: 20px; }
        .backdrop { display: none; }
      }
      @media (prefers-reduced-motion: reduce) { .dialog { animation: none; } }
    `;

    const backdrop = createElement('div', { className: 'backdrop' });
    const dialog = createElement('section', {
      className: 'dialog',
      role: 'dialog',
      'aria-modal': 'true',
      'aria-labelledby': 'reflection-title'
    });
    const eyebrow = createElement('p', { className: 'eyebrow' }, options.isTest ? 'Test prompt' : 'Session complete');
    const title = createElement('h2', { id: 'reflection-title' }, 'How did you spend your time?');
    const context = createElement(
      'p',
      { className: 'context' },
      options.isTest ? 'Test mode: this reflection will be saved to the test tab, not your template or daily tab.'
        : `${formatDuration(options.durationSeconds)} finished. Capture the result while it is fresh.`
    );
    const label = createElement('label', { for: 'reflection-response' }, 'Reflection');
    const textarea = createElement('textarea', {
      id: 'reflection-response',
      maxlength: String(MAX_REFLECTION_LENGTH),
      placeholder: 'What did you work on, finish, or learn?'
    });
    const meta = createElement('div', { className: 'meta' });
    const status = createElement('span', { className: 'status', role: 'status', 'aria-live': 'polite' });
    const count = createElement('span', {}, `0 / ${MAX_REFLECTION_LENGTH.toLocaleString()}`);
    const actions = createElement('div', { className: 'actions' });
    const skipButton = createElement('button', { className: 'skip', type: 'button' }, 'Skip');
    const settingsButton = createElement('button', { className: 'settings', type: 'button', hidden: '' }, 'Finish setup');
    const submitButton = createElement('button', { className: 'submit', type: 'button' }, 'Save reflection');

    meta.append(status, count);
    actions.append(skipButton, settingsButton, submitButton);
    dialog.append(eyebrow, title, context, label, textarea, meta, actions);
    shadow.append(style, backdrop, dialog);
    (document.body || document.documentElement).appendChild(host);
    log('prompt.shown');

    const setBusy = (busy) => {
      textarea.disabled = busy;
      skipButton.disabled = busy;
      settingsButton.disabled = busy;
      submitButton.disabled = busy;
      submitButton.textContent = busy ? 'Saving…' : 'Save reflection';
    };

    const dismiss = async () => {
      log('prompt.skipped');
      dismissLocalPrompt();
      if (!options.isTest) {
        try {
          await sendMessage({ action: 'dismissReflection' });
        } catch (_error) {
          // The prompt is already gone locally; background state can recover later.
        }
      }
    };

    const submit = async () => {
      const message = textarea.value.trim();
      status.className = 'status';
      if (!message) {
        status.textContent = 'Write something first.';
        status.classList.add('error');
        textarea.focus();
        return;
      }
      setBusy(true);
      log('prompt.submit');
      status.textContent = '';
      try {
        const response = await sendMessage({ action: 'saveReflection', message, isTest: options.isTest });
        log('prompt.saved');
        status.textContent = `Saved${response.data && response.data.sheet ? ` to ${response.data.sheet}` : ''}.`;
        status.classList.add('success');
        setTimeout(dismissLocalPrompt, 450);
      } catch (error) {
        log('prompt.failed', { errorKind: error.code === 'SETTINGS_REQUIRED' ? 'settings_required' : 'unknown' });
        setBusy(false);
        status.textContent = error.message;
        status.classList.add('error');
        settingsButton.hidden = error.code !== 'SETTINGS_REQUIRED';
        textarea.focus();
      }
    };

    textarea.addEventListener('input', () => {
      count.textContent = `${textarea.value.length.toLocaleString()} / ${MAX_REFLECTION_LENGTH.toLocaleString()}`;
    });
    textarea.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
        event.preventDefault();
        submit();
      }
    });
    skipButton.addEventListener('click', dismiss);
    settingsButton.addEventListener('click', () => {
      sendMessage({ action: 'openSettings' }).catch((error) => {
        status.textContent = error.message;
        status.classList.add('error');
      });
    });
    submitButton.addEventListener('click', submit);
    backdrop.addEventListener('click', () => textarea.focus());
    host.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        dismiss();
      }
    });

    setTimeout(() => textarea.focus(), 0);
    playNotificationSound();
  }

  chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
    if (message.action === 'showReflectionPrompt' || message.action === 'showTestChatbox') {
      if (document.getElementById(HOST_ID)) {
        if ((message.isTest || message.action === 'showTestChatbox') && !activePromptIsTest) {
          sendResponse({ success: false, error: 'A real reflection is already open. Save or skip it before using the test tab.' });
          return;
        }
        sendResponse({ success: true, alreadyVisible: true });
        return;
      }
      buildPrompt({
        isTest: Boolean(message.isTest || message.action === 'showTestChatbox'),
        sfxVolume: message.sfxVolume,
        durationSeconds: message.durationSeconds || (Number(message.minutes) * 60) || 1500
      });
      sendResponse({ success: true });
      return;
    }
    if (message.action === 'dismissReflectionPrompt' || (message.action === 'chatboxStateChanged' && !message.isVisible)) {
      dismissLocalPrompt();
      sendResponse({ success: true });
      return;
    }
    if (message.action === 'updateVolume') {
      sfxVolume = Math.max(0, Math.min(1, Number(message.volume) || 0));
      sendResponse({ success: true });
      return;
    }
    if (message.action === 'testSound') {
      sfxVolume = Math.max(0, Math.min(1, Number(message.volume) || 0));
      playNotificationSound();
      sendResponse({ success: true });
    }
  });

  sendMessage({ action: 'contentReady' }).catch(() => {});
})();
