// Listen for installation
chrome.runtime.onInstalled.addListener(() => {
  console.log('[Background] Extension installed');
  chrome.storage.local.get(['backgroundTimerState'], (result) => {
    if (result.backgroundTimerState) {
      const state = result.backgroundTimerState;
      timeLeft = state.timeLeft;
      isRunning = state.isRunning;
      originalTime = state.originalTime;
      autoRestartEnabled = state.autoRestartEnabled;
      
      if (isRunning) {
        startBackgroundTimer(timeLeft, autoRestartEnabled);
      }
    }
  });
});

// Function to inject content script
async function injectContentScript(tabId) {
  try {
    console.log('[Background] Attempting to inject content script into tab:', tabId);
    
    // Check if we can access the tab
    const tab = await chrome.tabs.get(tabId);
    if (!tab.url) {
      console.log('[Background] Tab URL is undefined, skipping injection');
      return false;
    }
    
    if (tab.url.startsWith('chrome://') || tab.url.startsWith('edge://')) {
      console.log('[Background] Cannot inject into browser page:', tab.url);
      return false;
    }

    // Inject CSS
    console.log('[Background] Injecting CSS...');
    await chrome.scripting.insertCSS({
      target: { tabId: tabId },
      files: ['styles.css']
    });
    
    // Inject JS
    console.log('[Background] Injecting JS...');
    await chrome.scripting.executeScript({
      target: { tabId: tabId },
      files: ['content.js']
    });
    
    console.log('[Background] Content script injected successfully');
    return true;
  } catch (error) {
    console.error('[Background] Error injecting content script:', error);
    return false;
  }
}

// Ensure content script is injected when needed
chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  // Only inject once the tab is complete and has a valid URL
  if (changeInfo.status === 'complete' && tab.url && !tab.url.startsWith('chrome://')) {
    console.log('[Background] Tab updated, injecting content script:', tabId);
    injectContentScript(tabId).catch(console.error);
  }
});

// Function to base64 encode string
function base64UrlEncode(str) {
  const base64 = btoa(encodeURIComponent(str).replace(/%([0-9A-F]{2})/g,
    function toChar(match, p1) {
      return String.fromCharCode('0x' + p1);
    }));
  return base64.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

// Function to get access token
async function getAccessToken(clientEmail, privateKey) {
  try {
    const now = Math.floor(Date.now() / 1000);
    const expiry = now + 3600; // Token valid for 1 hour

    const header = {
      alg: 'RS256',
      typ: 'JWT'
    };

    const claim = {
      iss: clientEmail,
      scope: 'https://www.googleapis.com/auth/spreadsheets',
      aud: 'https://oauth2.googleapis.com/token',
      exp: expiry,
      iat: now
    };

    // Create JWT
    const headerBase64 = base64UrlEncode(JSON.stringify(header));
    const claimBase64 = base64UrlEncode(JSON.stringify(claim));
    const signatureInput = `${headerBase64}.${claimBase64}`;
    
    // Sign the JWT using the private key
    const signature = await signJWT(signatureInput, privateKey);
    const jwt = `${signatureInput}.${signature}`;

    // Exchange JWT for access token
    const tokenResponse = await fetch('https://oauth2.googleapis.com/token', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/x-www-form-urlencoded'
      },
      body: new URLSearchParams({
        grant_type: 'urn:ietf:params:oauth:grant-type:jwt-bearer',
        assertion: jwt
      })
    });

    if (!tokenResponse.ok) {
      const errorText = await tokenResponse.text();
      throw new Error(`Token error! status: ${tokenResponse.status}, body: ${errorText}`);
    }

    const tokenData = await tokenResponse.json();
    return tokenData.access_token;
  } catch (error) {
    console.error('Error getting access token:', error);
    throw error;
  }
}

// Function to sign JWT
async function signJWT(input, privateKey) {
  try {
    // Convert PEM private key to CryptoKey
    const pemHeader = '-----BEGIN PRIVATE KEY-----';
    const pemFooter = '-----END PRIVATE KEY-----';
    const pemContents = privateKey
      .replace(pemHeader, '')
      .replace(pemFooter, '')
      .replace(/\\n/g, '')
      .trim();
    
    // Decode the base64 key properly
    const binaryDer = Uint8Array.from(atob(pemContents), c => c.charCodeAt(0));
    
    const cryptoKey = await crypto.subtle.importKey(
      'pkcs8',
      binaryDer,
      {
        name: 'RSASSA-PKCS1-v1_5',
        hash: 'SHA-256'
      },
      false,
      ['sign']
    );

    // Sign the input
    const encoder = new TextEncoder();
    const signatureBuffer = await crypto.subtle.sign(
      'RSASSA-PKCS1-v1_5',
      cryptoKey,
      encoder.encode(input)
    );

    // Convert signature to base64url
    return btoa(String.fromCharCode(...new Uint8Array(signatureBuffer)))
      .replace(/\+/g, '-')
      .replace(/\//g, '_')
      .replace(/=+$/, '');
  } catch (error) {
    console.error('Error signing JWT:', error);
    throw error;
  }
}

// Function to update Google Sheet
async function updateGoogleSheet(credentials, message) {
  try {
    const { clientEmail, privateKey, spreadsheetId } = credentials;
    
    if (!clientEmail || !privateKey || !spreadsheetId) {
      throw new Error('Missing required credentials');
    }
    
    // Get access token
    const token = await getAccessToken(clientEmail, privateKey);
    
    // Update cell A1 with the message
    const response = await fetch(
      `https://sheets.googleapis.com/v4/spreadsheets/${spreadsheetId}/values/A1?valueInputOption=RAW`,
      {
        method: 'PUT',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          values: [[message]]
        })
      }
    );

    if (!response.ok) {
      const errorText = await response.text();
      throw new Error(`HTTP error! status: ${response.status}, body: ${errorText}`);
    }

    const data = await response.json();
    console.log('Cell A1 updated successfully:', data);
    return data;
  } catch (error) {
    console.error('Error updating Google Sheet:', error);
    throw error;
  }
}

// Timer state
let timer = null;
let timeLeft = 0;
let isRunning = false;
let originalTime = null;
let autoRestartEnabled = false;
let chatboxShown = false; // Track if chatbox has been shown for current countdown

// Function to update timer state
function updateTimerState() {
  chrome.storage.local.set({
    backgroundTimerState: {
      timeLeft,
      isRunning,
      originalTime,
      autoRestartEnabled
    }
  });
  
  // Broadcast timer update to any open popups
  chrome.runtime.sendMessage({
    action: 'timerUpdate',
    timeLeft,
    isRunning
  });
}

// Function to start timer
function startBackgroundTimer(initialTime, autoRestart = false) {
  if (timer) {
    clearInterval(timer);
  }
  
  // Ensure initialTime is a number and greater than 0
  timeLeft = Math.max(0, parseInt(initialTime) || 0);
  isRunning = timeLeft > 0;
  originalTime = timeLeft;
  autoRestartEnabled = autoRestart;
  chatboxShown = false; // Reset chatbox flag when timer starts
  
  if (!isRunning) {
    updateTimerState();
    return;
  }
  
  timer = setInterval(() => {
    if (timeLeft > 0) {
      timeLeft--;
      
      // Show chatbox at 30 seconds if not already shown
      if (timeLeft === 30 && !chatboxShown) {
        chatboxShown = true;
        // Send message to all tabs to show chatbox
        chrome.tabs.query({}, (tabs) => {
          tabs.forEach(tab => {
            if (!tab.url.startsWith('chrome://')) {
              chrome.tabs.sendMessage(tab.id, {
                action: 'showReflectionChatBox',
                minutes: 0.5,
                timeLeft: timeLeft
              }).catch(() => {
                // Ignore errors for tabs that don't have the content script
              });
            }
          });
        });
      }
      
      updateTimerState();
    }
    
    // When timer reaches 0
    if (timeLeft === 0) {
      // Clear the current interval
      clearInterval(timer);
      timer = null;
      
      if (autoRestartEnabled) {
        // If auto-restart is enabled, restart the timer with original time
        console.log('[Background] Timer completed, auto-restarting with original time:', originalTime);
        
        // Play notification sound if available
        chrome.tabs.query({}, (tabs) => {
          tabs.forEach(tab => {
            if (!tab.url.startsWith('chrome://')) {
              chrome.tabs.sendMessage(tab.id, {
                action: 'playTimerComplete'
              }).catch(() => {
                // Ignore errors for tabs that don't have the content script
              });
            }
          });
        });
        
        // Start a new timer with the original time
        startBackgroundTimer(originalTime, autoRestartEnabled);
      } else {
        // If auto-restart is disabled, stop the timer
        stopBackgroundTimer();
        timeLeft = originalTime;
        updateTimerState();
      }
    }
  }, 1000);
  
  updateTimerState();
}

// Function to update auto-restart setting without restarting timer
function updateAutoRestart(enabled) {
  autoRestartEnabled = enabled;
  updateTimerState();
}

// Function to stop timer
function stopBackgroundTimer() {
  if (timer) {
    clearInterval(timer);
    timer = null;
  }
  isRunning = false;
  updateTimerState();
}

// Function to reset timer
function resetBackgroundTimer() {
  if (timer) {
    clearInterval(timer);
    timer = null;
  }
  timeLeft = originalTime || 0;
  isRunning = false;
  updateTimerState();
}

// Listen for messages from content script or popup
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.action === 'updateGoogleSheet') {
    // Get credentials from storage
    chrome.storage.local.get([
      'GOOGLE_SHEETS_CLIENT_EMAIL',
      'GOOGLE_SHEETS_PRIVATE_KEY',
      'SPREADSHEET_ID'
    ], async (result) => {
      try {
        // Check if credentials exist
        if (!result.GOOGLE_SHEETS_CLIENT_EMAIL || !result.GOOGLE_SHEETS_PRIVATE_KEY || !result.SPREADSHEET_ID) {
          throw new Error('Missing Google Sheets credentials. Please check the extension settings.');
        }

        const data = await updateGoogleSheet({
          clientEmail: result.GOOGLE_SHEETS_CLIENT_EMAIL,
          privateKey: result.GOOGLE_SHEETS_PRIVATE_KEY,
          spreadsheetId: result.SPREADSHEET_ID
        }, message.message);

        sendResponse({ success: true, data: data });
      } catch (error) {
        console.error('Error updating Google Sheet:', error);
        sendResponse({ success: false, error: error.message });
      }
    });
    return true; // Keep the message channel open for async response
  } else if (message.action === 'injectContentScript') {
    // Handle the injection
    chrome.tabs.query({ active: true, currentWindow: true }, async (tabs) => {
      if (tabs[0] && !tabs[0].url.startsWith('chrome://')) {
        try {
          await injectContentScript(tabs[0].id);
          sendResponse({ success: true });
        } catch (error) {
          console.error('[Background] Error during injection:', error);
          sendResponse({ success: false, error: error.message });
        }
      } else {
        console.error('[Background] No valid active tab found');
        sendResponse({ success: false, error: 'No valid active tab found' });
      }
    });
    return true; // Keep the message channel open for async response
  } else if (message.action === 'scheduleTimer') {
    // Handle timer scheduling
    const targetTime = new Date(message.targetTime);
    chrome.alarms.create('startTimer', { when: targetTime.getTime() });
    chrome.storage.local.set({
      scheduledTimer: {
        targetTime: targetTime.toISOString(),
        originalTime: message.originalTime
      }
    });
    sendResponse({ success: true });
    return true;
  } else if (message.action === 'clearScheduledTimer') {
    chrome.alarms.clearAll();
    chrome.storage.local.remove('scheduledTimer');
    sendResponse({ success: true });
    return true;
  } else if (message.action === 'startTimer') {
    startBackgroundTimer(message.timeLeft, message.autoRestart);
    sendResponse({ success: true });
    return true;
  } else if (message.action === 'stopTimer') {
    stopBackgroundTimer();
    sendResponse({ success: true });
    return true;
  } else if (message.action === 'resetTimer') {
    resetBackgroundTimer();
    sendResponse({ success: true });
    return true;
  } else if (message.action === 'getTimerState') {
    sendResponse({
      timeLeft,
      isRunning,
      originalTime,
      autoRestartEnabled
    });
    return true;
  } else if (message.action === 'updateAutoRestart') {
    updateAutoRestart(message.autoRestart);
    sendResponse({ success: true });
    return true;
  }
});

// Handle alarm triggers
chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === 'startTimer') {
    console.log('[Background] Alarm triggered at:', new Date().toLocaleString());
    
    // Get the stored timer settings
    chrome.storage.local.get(['scheduledTimer', 'autoRestart', 'startAtTimeSettings'], (result) => {
      if (result.scheduledTimer) {
        const { originalTime } = result.scheduledTimer;
        
        // Calculate total seconds from original time
        const totalSeconds = 
          (parseInt(originalTime.hours) || 0) * 3600 + 
          (parseInt(originalTime.minutes) || 0) * 60 + 
          (parseInt(originalTime.seconds) || 0);
        
        // Start the timer directly in the background with auto-restart setting
        startBackgroundTimer(totalSeconds, result.autoRestart || false);
        
        // Update storage to reflect that the scheduled timer has started and uncheck the checkbox
        chrome.storage.local.remove(['scheduledTimer', 'scheduledState']);
        
        // Uncheck the "Start at This Time" checkbox by updating its settings
        if (result.startAtTimeSettings) {
          const updatedSettings = {
            ...result.startAtTimeSettings,
            enabled: false
          };
          chrome.storage.local.set({ startAtTimeSettings: updatedSettings });
        }
        
        // Show a notification that the timer has started
        chrome.notifications.create('timerStarted', {
          type: 'basic',
          iconUrl: 'icon48.png',
          title: 'Timer Started',
          message: `Your scheduled timer has started!`
        });
        
        // Update any open popups
        chrome.runtime.sendMessage({
          action: 'startScheduledTimer',
          originalTime: originalTime
        });
      }
    });
  }
});

// Initialize timer state when extension loads
chrome.runtime.onStartup.addListener(() => {
  // Check for both background timer state and scheduled timer
  chrome.storage.local.get(['backgroundTimerState', 'scheduledTimer', 'scheduledState'], (result) => {
    // First check for scheduled timer
    if (result.scheduledTimer && result.scheduledState) {
      const targetTime = new Date(result.scheduledState.targetTime);
      const now = new Date();
      
      if (targetTime > now) {
        // Re-create the alarm if the scheduled time hasn't passed
        chrome.alarms.create('startTimer', { when: targetTime.getTime() });
      } else {
        // Clean up if the scheduled time has passed
        chrome.storage.local.remove(['scheduledTimer', 'scheduledState']);
      }
    }
    
    // Then check for running timer state
    if (result.backgroundTimerState) {
      const state = result.backgroundTimerState;
      timeLeft = state.timeLeft;
      isRunning = state.isRunning;
      originalTime = state.originalTime;
      autoRestartEnabled = state.autoRestartEnabled;
      
      if (isRunning) {
        startBackgroundTimer(timeLeft, autoRestartEnabled);
      }
    }
  });
});

// Also check timer state when installed
chrome.runtime.onInstalled.addListener(() => {
  chrome.storage.local.get(['backgroundTimerState', 'scheduledTimer', 'scheduledState'], (result) => {
    // First check for scheduled timer
    if (result.scheduledTimer && result.scheduledState) {
      const targetTime = new Date(result.scheduledState.targetTime);
      const now = new Date();
      
      if (targetTime > now) {
        // Re-create the alarm if the scheduled time hasn't passed
        chrome.alarms.create('startTimer', { when: targetTime.getTime() });
      } else {
        // Clean up if the scheduled time has passed
        chrome.storage.local.remove(['scheduledTimer', 'scheduledState']);
      }
    }
    
    // Then check for running timer state
    if (result.backgroundTimerState) {
      const state = result.backgroundTimerState;
      timeLeft = state.timeLeft;
      isRunning = state.isRunning;
      originalTime = state.originalTime;
      autoRestartEnabled = state.autoRestartEnabled;
      
      if (isRunning) {
        startBackgroundTimer(timeLeft, autoRestartEnabled);
      }
    }
  });
}); 