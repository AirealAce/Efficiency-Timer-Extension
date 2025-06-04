document.addEventListener('DOMContentLoaded', () => {
  const clientEmailInput = document.getElementById('clientEmail');
  const privateKeyInput = document.getElementById('privateKey');
  const spreadsheetIdInput = document.getElementById('spreadsheetId');
  const saveButton = document.getElementById('saveSettings');
  const statusDiv = document.getElementById('status');

  // Load existing settings
  chrome.storage.local.get([
    'GOOGLE_SHEETS_CLIENT_EMAIL',
    'GOOGLE_SHEETS_PRIVATE_KEY',
    'SPREADSHEET_ID'
  ], (result) => {
    if (result.GOOGLE_SHEETS_CLIENT_EMAIL) {
      clientEmailInput.value = result.GOOGLE_SHEETS_CLIENT_EMAIL;
    }
    if (result.GOOGLE_SHEETS_PRIVATE_KEY) {
      privateKeyInput.value = result.GOOGLE_SHEETS_PRIVATE_KEY;
    }
    if (result.SPREADSHEET_ID) {
      spreadsheetIdInput.value = result.SPREADSHEET_ID;
    }
  });

  function showStatus(message, isError = false) {
    statusDiv.textContent = message;
    statusDiv.style.display = 'block';
    statusDiv.className = `status ${isError ? 'error' : 'success'}`;
    setTimeout(() => {
      statusDiv.style.display = 'none';
    }, 3000);
  }

  saveButton.addEventListener('click', () => {
    const clientEmail = clientEmailInput.value.trim();
    const privateKey = privateKeyInput.value.trim();
    const spreadsheetId = spreadsheetIdInput.value.trim();

    if (!clientEmail || !privateKey || !spreadsheetId) {
      showStatus('Please fill in all fields', true);
      return;
    }

    chrome.storage.local.set({
      GOOGLE_SHEETS_CLIENT_EMAIL: clientEmail,
      GOOGLE_SHEETS_PRIVATE_KEY: privateKey,
      SPREADSHEET_ID: spreadsheetId
    }, () => {
      if (chrome.runtime.lastError) {
        showStatus('Error saving settings: ' + chrome.runtime.lastError.message, true);
      } else {
        showStatus('Settings saved successfully!');
      }
    });
  });
}); 