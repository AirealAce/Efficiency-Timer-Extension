'use strict';

document.addEventListener('DOMContentLoaded', () => {
  const DEFAULT_SHEET_URL = 'https://docs.google.com/spreadsheets/d/synthetic-spreadsheet-id-for-tests/edit';
  const form = document.getElementById('settingsForm');
  const sheetUrl = document.getElementById('sheetUrl');
  const sheetName = document.getElementById('sheetName');
  const webAppUrl = document.getElementById('webAppUrl');
  const apiToken = document.getElementById('apiToken');
  const saveButton = document.getElementById('save');
  const testButton = document.getElementById('test');
  const toggleTokenButton = document.getElementById('toggleToken');
  const generateTokenButton = document.getElementById('generateToken');
  const status = document.getElementById('status');

  function setStatus(message, type = '') {
    status.textContent = message;
    status.className = `status ${type}`.trim();
  }

  function sendMessage(message) {
    return new Promise((resolve, reject) => {
      chrome.runtime.sendMessage(message, (response) => {
        if (chrome.runtime.lastError) {
          reject(new Error(chrome.runtime.lastError.message));
        } else if (!response || response.success === false) {
          reject(new Error((response && response.error) || 'The extension did not respond.'));
        } else {
          resolve(response);
        }
      });
    });
  }

  function validate() {
    if (!TimerUtils.extractSpreadsheetId(sheetUrl.value)) {
      throw new Error('Enter a valid Google Sheets URL.');
    }
    if (!sheetName.value.trim()) {
      throw new Error('Enter the target tab name.');
    }
    if (!TimerUtils.isValidWebAppUrl(webAppUrl.value)) {
      throw new Error('Enter the deployed Apps Script URL ending in /exec.');
    }
    if (apiToken.value.trim().length < 16) {
      throw new Error('Use an API token of at least 16 characters.');
    }
  }

  async function saveSettings() {
    validate();
    await chrome.storage.local.set({
      sheetUrl: sheetUrl.value.trim(),
      sheetName: sheetName.value.trim(),
      webAppUrl: webAppUrl.value.trim(),
      apiToken: apiToken.value.trim()
    });
    await chrome.storage.local.remove([
      'GOOGLE_SHEETS_CLIENT_EMAIL',
      'GOOGLE_SHEETS_PRIVATE_KEY',
      'SPREADSHEET_ID',
      'sheetsLink'
    ]);
  }

  async function runTest() {
    saveButton.disabled = true;
    testButton.disabled = true;
    setStatus('Testing the connection…');
    try {
      await saveSettings();
      const response = await sendMessage({ action: 'testSheetsConnection' });
      const target = response.data && response.data.target;
      setStatus(`Connection works${target ? ` · ${target}` : ''}.`, 'success');
    } catch (error) {
      setStatus(error.message, 'error');
    } finally {
      saveButton.disabled = false;
      testButton.disabled = false;
    }
  }

  chrome.storage.local.get(['sheetUrl', 'sheetsLink', 'sheetName', 'webAppUrl', 'apiToken']).then((saved) => {
    sheetUrl.value = saved.sheetUrl || saved.sheetsLink || DEFAULT_SHEET_URL;
    sheetName.value = saved.sheetName || 'Template';
    webAppUrl.value = saved.webAppUrl || '';
    apiToken.value = saved.apiToken || '';
    if (!TimerUtils.isValidWebAppUrl(webAppUrl.value) || apiToken.value.trim().length < 16) {
      setStatus('Your Sheet is selected. Complete the Apps Script URL and matching token below, then choose Save & test.');
    }
  }).catch((error) => setStatus(error.message, 'error'));

  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    saveButton.disabled = true;
    setStatus('');
    try {
      await saveSettings();
      setStatus('Settings saved.', 'success');
    } catch (error) {
      setStatus(error.message, 'error');
    } finally {
      saveButton.disabled = false;
    }
  });

  testButton.addEventListener('click', runTest);

  toggleTokenButton.addEventListener('click', () => {
    const show = apiToken.type === 'password';
    apiToken.type = show ? 'text' : 'password';
    toggleTokenButton.textContent = show ? 'Hide' : 'Show';
  });

  generateTokenButton.addEventListener('click', () => {
    const bytes = crypto.getRandomValues(new Uint8Array(32));
    apiToken.value = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
    apiToken.type = 'text';
    toggleTokenButton.textContent = 'Hide';
    apiToken.focus();
    apiToken.select();
    setStatus('Token generated. Copy it into the Apps Script property, then save.', 'success');
  });
});
