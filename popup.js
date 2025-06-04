let timer;
let timeLeft;
let isRunning = false;

// Initialize all UI elements
const startStopBtn = document.getElementById('startStop');
const resetBtn = document.getElementById('reset');
const hoursInput = document.getElementById('hours');
const minutesInput = document.getElementById('minutes');
const secondsInput = document.getElementById('seconds');
const display = document.getElementById('display');
const autoRestartCheck = document.getElementById('autoRestart');
const darkModeToggle = document.getElementById('darkModeToggle');
const volumeSlider = document.getElementById('volumeSlider');
const sunIcon = darkModeToggle.querySelector('.sun-icon');
const moonIcon = darkModeToggle.querySelector('.moon-icon');
const startAtTimeCheck = document.getElementById('startAtTime');
const startTimeInput = document.getElementById('startTime');
const currentTimeDisplay = document.getElementById('currentTime');
const sheetsLinkInput = document.getElementById('sheetsLink');
const webAppUrlInput = document.getElementById('webAppUrl');
const openSettingsBtn = document.getElementById('openSettings');
const testChatboxBtn = document.getElementById('testChatbox');

// Set icon URLs
moonIcon.src = chrome.runtime.getURL('half_moon.webp');
sunIcon.src = chrome.runtime.getURL('sun-emoji-2048x2048-1je5hwoj.png');

// Function to update current time display
function updateCurrentTime() {
  const now = new Date();
  const hours = String(now.getHours()).padStart(2, '0');
  const minutes = String(now.getMinutes()).padStart(2, '0');
  const seconds = String(now.getSeconds()).padStart(2, '0');
  currentTimeDisplay.textContent = `${hours}:${minutes}:${seconds}`;
}

// Update current time immediately and then every second
updateCurrentTime();
setInterval(updateCurrentTime, 1000);

// Initialize timeLeft
timeLeft = calculateTotalSeconds();

// Load saved values
chrome.storage.local.get(['timerValues', 'sfxVolume', 'startAtTimeSettings', 'sheetsLink', 'webAppUrl', 'autoRestart'], (result) => {
  // Restore volume
  const volume = result.sfxVolume ?? 50;
  volumeSlider.value = volume;
  
  // Restore auto-restart setting
  if (result.autoRestart !== undefined) {
    autoRestartCheck.checked = result.autoRestart;
  }
  
  // Add volume slider change handler
  volumeSlider.addEventListener('input', () => {
    const volume = volumeSlider.value;
    chrome.storage.local.set({ sfxVolume: volume });
    
    // Send volume update to all tabs
    chrome.tabs.query({}, (tabs) => {
      tabs.forEach(tab => {
        chrome.tabs.sendMessage(tab.id, {
          action: 'updateVolume',
          volume: volume / 100
        }).catch(() => {
          // Ignore errors for tabs that don't have the content script
        });
      });
    });
  });

  // Add volume slider release handler for test sound
  volumeSlider.addEventListener('change', () => {
    // Get the current active tab
    chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
      if (tabs[0]) {
        chrome.tabs.sendMessage(tabs[0].id, {
          action: 'testSound',
          volume: volumeSlider.value / 100
        }).catch(() => {
          // Ignore errors if content script is not loaded
        });
      }
    });
  });
  
  // Restore timer values
  if (result.timerValues) {
    hoursInput.value = result.timerValues.hours || '';
    minutesInput.value = result.timerValues.minutes || '';
    secondsInput.value = result.timerValues.seconds || '';
    timeLeft = calculateTotalSeconds();
    updateDisplay();
  } else {
    // Set default values if no saved values
    minutesInput.value = '25';
    timeLeft = 25 * 60;
    updateDisplay();
  }

  // Restore start at time settings
  if (result.startAtTimeSettings) {
    startAtTimeCheck.checked = result.startAtTimeSettings.enabled;
    startTimeInput.value = result.startAtTimeSettings.time;
    startTimeInput.disabled = !result.startAtTimeSettings.enabled;
  }

  // Restore sheets link
  if (result.sheetsLink) {
    sheetsLinkInput.value = result.sheetsLink;
    console.log('[Popup] Loaded saved sheets link');
  }

  // Restore web app URL
  if (result.webAppUrl) {
    webAppUrlInput.value = result.webAppUrl;
    console.log('[Popup] Loaded saved web app URL');
  }
});

// Save timer values when changed
function saveTimerValues() {
  const values = {
    hours: hoursInput.value || 0,
    minutes: minutesInput.value || 0,
    seconds: secondsInput.value || 0
  };
  chrome.storage.local.set({ timerValues: values });
}

// Save sheets link when changed
if (sheetsLinkInput) {
  sheetsLinkInput.addEventListener('change', () => {
    const link = sheetsLinkInput.value.trim();
    chrome.storage.local.set({ sheetsLink: link }, () => {
      console.log('[Popup] Saved sheets link:', link);
    });
  });

  // Also save on input to handle paste events
  sheetsLinkInput.addEventListener('input', () => {
    const link = sheetsLinkInput.value.trim();
    chrome.storage.local.set({ sheetsLink: link }, () => {
      console.log('[Popup] Saved sheets link:', link);
    });
  });
}

// Save web app URL when changed
if (webAppUrlInput) {
  webAppUrlInput.addEventListener('change', () => {
    const url = webAppUrlInput.value.trim();
    chrome.storage.local.set({ webAppUrl: url }, () => {
      console.log('[Popup] Saved web app URL:', url);
    });
  });

  // Also save on input to handle paste events
  webAppUrlInput.addEventListener('input', () => {
    const url = webAppUrlInput.value.trim();
    chrome.storage.local.set({ webAppUrl: url }, () => {
      console.log('[Popup] Saved web app URL:', url);
    });
  });
}

// Timer controls
startStopBtn.addEventListener('click', () => {
  if (startAtTimeCheck.checked) {
    if (startStopBtn.textContent === 'Set') {
      checkStartTime();
      return;
    } else if (startStopBtn.textContent === 'Change') {
      // Allow changing the scheduled time
      startStopBtn.textContent = 'Set';
      startStopBtn.disabled = false;
      return;
    } else if (startStopBtn.textContent === 'Scheduled') {
      // Clear the scheduled timer if user wants to change it
      chrome.runtime.sendMessage({ action: 'clearScheduledTimer' });
      chrome.storage.local.remove('scheduledState');
      startStopBtn.textContent = 'Set';
      startStopBtn.disabled = false;
      return;
    }
  }
  
  if (isRunning) {
    stopTimer();
  } else {
    startTimer();
  }
});

resetBtn.addEventListener('click', resetTimer);

// Update display when time inputs change
[hoursInput, minutesInput, secondsInput].forEach((input, index) => {
  input.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      if (e.ctrlKey) {
        // If Ctrl+Enter is pressed, start/stop timer immediately
        input.blur();
        if (isRunning) {
          stopTimer();
        } else {
          startTimer();
        }
      } else if (index < 2) {
        // If not on the last input, move to next input
        [hoursInput, minutesInput, secondsInput][index + 1].focus();
      } else {
        // On the last input (seconds), start the timer
        input.blur();
        if (!isRunning) {
          startTimer();
        }
      }
    }
  });
  
  input.addEventListener('change', () => {
    if (input.value < 0) input.value = 0;
    timeLeft = calculateTotalSeconds();
    updateDisplay();
    saveTimerValues();
  });
  
  // Add input event listener to update display in real-time
  input.addEventListener('input', () => {
    timeLeft = calculateTotalSeconds();
    updateDisplay();
  });
});

function calculateTotalSeconds() {
  const hours = parseInt(hoursInput.value) || 0;
  const minutes = parseInt(minutesInput.value) || 0;
  const seconds = parseInt(secondsInput.value) || 0;
  return (hours * 3600) + (minutes * 60) + seconds;
}

function startTimer() {
  // Recalculate timeLeft from current input values
  timeLeft = calculateTotalSeconds();
  
  if (timeLeft <= 0) return;
  
  // Check if we should wait for start time
  if (startAtTimeCheck.checked && startTimeInput.value) {
    checkStartTime();
    return;
  }
  
  // Save current timer values before starting
  saveTimerValues();
  
  chrome.runtime.sendMessage({
    action: 'startTimer',
    timeLeft: timeLeft,
    autoRestart: autoRestartCheck.checked
  }, (response) => {
    if (response && response.success) {
      isRunning = true;
      startStopBtn.textContent = 'Stop';
    }
  });
}

// Function to show chatbox in active tab
async function showChatboxInActiveTab() {
  try {
    const tabs = await chrome.tabs.query({active: true, currentWindow: true});
    if (!tabs || !tabs[0]) {
      console.error('No active tab found');
      return false;
    }
    
    // Single attempt with promise
    return new Promise((resolve) => {
      chrome.tabs.sendMessage(tabs[0].id, {
        action: 'showReflectionChatBox',
        minutes: 0.5, // 30 seconds = 0.5 minutes
        timeLeft: timeLeft,
        sheetsLink: sheetsLinkInput.value // Add sheets link to the message
      }, (response) => {
        if (chrome.runtime.lastError) {
          console.error('Error sending message:', chrome.runtime.lastError);
          resolve(false);
        } else if (!response || !response.success) {
          console.error('Failed to show chatbox:', response);
          resolve(false);
        } else {
          console.log('Chatbox shown successfully:', response);
          resolve(true);
        }
      });
    });
  } catch (error) {
    console.error('Error in showChatboxInActiveTab:', error);
    return false;
  }
}

function stopTimer() {
  chrome.runtime.sendMessage({ action: 'stopTimer' }, (response) => {
    if (response && response.success) {
      isRunning = false;
      startStopBtn.textContent = startAtTimeCheck.checked ? 'Set' : 'Start';
    }
  });
  
  // Clear any scheduled timer in the background
  chrome.runtime.sendMessage({ action: 'clearScheduledTimer' });
  startStopBtn.disabled = false;
}

function resetTimer() {
  chrome.runtime.sendMessage({ action: 'resetTimer' }, (response) => {
    if (response && response.success) {
      timeLeft = calculateTotalSeconds();
      isRunning = false;
      updateDisplay();
      startStopBtn.textContent = startAtTimeCheck.checked ? 'Set' : 'Start';
    }
  });
}

function updateDisplay() {
  const hours = Math.floor(timeLeft / 3600);
  const minutes = Math.floor((timeLeft % 3600) / 60);
  const seconds = timeLeft % 60;
  
  const displayStr = [
    hours > 0 ? hours.toString() : '',
    minutes.toString().padStart(2, '0'),
    seconds.toString().padStart(2, '0')
  ].filter(Boolean).join(':');
  
  display.textContent = displayStr;
}

function timerComplete() {
  stopTimer();
  
  if (autoRestartCheck.checked) {
    resetTimer();
    startTimer();
  } else {
    resetTimer();
  }
}

// Dark mode toggle click handler
darkModeToggle.addEventListener('click', () => {
  const isDark = document.body.classList.contains('dark-mode');
  if (isDark) {
    document.body.classList.remove('dark-mode');
    document.body.classList.add('light-mode');
    chrome.storage.local.set({ darkMode: 'disabled' });
  } else {
    document.body.classList.remove('light-mode');
    document.body.classList.add('dark-mode');
    chrome.storage.local.set({ darkMode: 'enabled' });
  }
  updateDarkModeIcons(!isDark);
  
  // Notify all tabs about the theme change
  chrome.tabs.query({}, function(tabs) {
    tabs.forEach(tab => {
      chrome.tabs.sendMessage(tab.id, {
        action: 'themeChanged',
        isDark: !isDark
      }).catch(() => {
        // Ignore errors for tabs that don't have the content script
      });
    });
  });
});

// Listen for messages from background script
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.action === 'startScheduledTimer') {
    // Restore the original timer values
    if (message.originalTime) {
      hoursInput.value = message.originalTime.hours || '';
      minutesInput.value = message.originalTime.minutes || '';
      secondsInput.value = message.originalTime.seconds || '';
      timeLeft = calculateTotalSeconds();
    }
    startTimer();
    startAtTimeCheck.checked = false;
    startTimeInput.disabled = true;
    startStopBtn.textContent = 'Stop';
    startStopBtn.disabled = false;
    saveStartAtTimeSettings();
  }
  if (message.action === 'timerUpdate') {
    timeLeft = message.timeLeft;
    isRunning = message.isRunning;
    updateDisplay();
    startStopBtn.textContent = isRunning ? 'Stop' : (startAtTimeCheck.checked ? 'Set' : 'Start');
  }
});

function checkStartTime() {
  if (!startAtTimeCheck.checked || !startTimeInput.value) return;

  // Parse the time input value (format: HH:mm)
  const [inputHours, inputMinutes] = startTimeInput.value.split(':').map(Number);
  
  // Get current time in local timezone
  const now = new Date();
  
  // Create target time for today
  const targetTime = new Date();
  targetTime.setHours(inputHours, inputMinutes, 0, 0);
  
  // If time has passed for today, schedule for tomorrow
  if (targetTime <= now) {
    console.log('[Popup] Time has passed for today, scheduling for tomorrow');
    targetTime.setDate(targetTime.getDate() + 1);
  }

  // Save current timer values
  const originalTime = {
    hours: hoursInput.value || '',
    minutes: minutesInput.value || '',
    seconds: secondsInput.value || ''
  };

  console.log('[Popup] Scheduling timer for:', targetTime.toLocaleString());

  // Schedule the timer using chrome.alarms
  chrome.runtime.sendMessage({
    action: 'scheduleTimer',
    targetTime: targetTime.toISOString(),
    originalTime: originalTime
  }, (response) => {
    if (response && response.success) {
      startStopBtn.textContent = 'Scheduled';
      startStopBtn.disabled = true;
      
      // Store the scheduled state
      chrome.storage.local.set({
        scheduledState: {
          isScheduled: true,
          targetTime: targetTime.toISOString()
        },
        scheduledTimer: {
          originalTime: originalTime
        }
      });

      // Show a notification that the timer has been scheduled
      const timeStr = targetTime.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
      const dateStr = targetTime.toLocaleDateString();
      chrome.notifications.create('timerScheduled', {
        type: 'basic',
        iconUrl: 'icon48.png',
        title: 'Timer Scheduled',
        message: `Timer will start at ${timeStr} on ${dateStr}`
      });
    }
  });
}

// When popup opens, check if there's a scheduled timer
document.addEventListener('DOMContentLoaded', () => {
  // First load saved timer values and settings
  chrome.storage.local.get(['timerValues', 'scheduledState', 'autoRestart'], (result) => {
    // Restore timer values if they exist
    if (result.timerValues) {
      hoursInput.value = result.timerValues.hours || '';
      minutesInput.value = result.timerValues.minutes || '';
      secondsInput.value = result.timerValues.seconds || '';
      timeLeft = calculateTotalSeconds();
      updateDisplay();
    }

    // Restore auto-restart setting
    if (result.autoRestart !== undefined) {
      autoRestartCheck.checked = result.autoRestart;
    }

    // Then check if there's an active timer or scheduled timer
    chrome.runtime.sendMessage({ action: 'getTimerState' }, (response) => {
      if (response && response.isRunning) {
        // If timer is running, use the background timer state
        timeLeft = response.timeLeft;
        isRunning = response.isRunning;
        updateDisplay();
        startStopBtn.textContent = 'Stop';
      } else {
        // If no timer is running, keep the saved/default values
        startStopBtn.textContent = 'Start';
      }
    });
    
    // Check for scheduled timer state
    if (result.scheduledState && result.scheduledState.isScheduled) {
      const targetTime = new Date(result.scheduledState.targetTime);
      const now = new Date();
      
      if (targetTime > now) {
        startStopBtn.textContent = 'Scheduled';
        startStopBtn.disabled = true;
      } else {
        // Clear the scheduled state if the time has passed
        chrome.storage.local.remove('scheduledState');
      }
    }
  });
});

// Handle start at time checkbox
startAtTimeCheck.addEventListener('change', () => {
  startTimeInput.disabled = !startAtTimeCheck.checked;
  if (startAtTimeCheck.checked) {
    if (!startTimeInput.value) {
      const now = new Date();
      const timeStr = `${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`;
      startTimeInput.value = timeStr;
    }
    startStopBtn.textContent = 'Set';
  } else {
    // Clear any scheduled timer
    chrome.runtime.sendMessage({ action: 'clearScheduledTimer' });
    chrome.storage.local.remove('scheduledState');
    startStopBtn.disabled = false;
    startStopBtn.textContent = isRunning ? 'Stop' : 'Start';
  }
  saveStartAtTimeSettings();
});

// Handle start time input changes
startTimeInput.addEventListener('change', () => {
  if (startAtTimeCheck.checked) {
    // Clear any scheduled timer
    chrome.runtime.sendMessage({ action: 'clearScheduledTimer' });
    chrome.storage.local.remove('scheduledState');
    startStopBtn.textContent = 'Set';
    startStopBtn.disabled = false;
  }
  saveStartAtTimeSettings();
});

function saveStartAtTimeSettings() {
  const settings = {
    enabled: startAtTimeCheck.checked,
    time: startTimeInput.value
  };
  chrome.storage.local.set({ startAtTimeSettings: settings });
}

// Dark mode toggle
function updateDarkModeIcons(isDark) {
  console.log('[Popup] Updating dark mode icons, isDark:', isDark);
  console.log('[Popup] Sun icon element:', sunIcon);
  console.log('[Popup] Moon icon element:', moonIcon);
  
  // Check if images are loaded
  moonIcon.onload = () => console.log('[Popup] Moon icon loaded successfully');
  moonIcon.onerror = (e) => console.error('[Popup] Moon icon failed to load:', e);
  sunIcon.onload = () => console.log('[Popup] Sun icon loaded successfully');
  sunIcon.onerror = (e) => console.error('[Popup] Sun icon failed to load:', e);
  
  sunIcon.style.display = isDark ? 'block' : 'none';
  moonIcon.style.display = isDark ? 'none' : 'block';
  
  // Log the current src attributes
  console.log('[Popup] Sun icon src:', sunIcon.src);
  console.log('[Popup] Moon icon src:', moonIcon.src);
}

// Initialize dark mode on load
document.addEventListener('DOMContentLoaded', () => {
  console.log('[Popup] DOM Content Loaded');
  chrome.storage.local.get(['darkMode'], (result) => {
    const isDark = result.darkMode === 'enabled';
    if (isDark) {
      document.body.classList.add('dark-mode');
      document.body.classList.remove('light-mode');
    } else {
      document.body.classList.add('light-mode');
      document.body.classList.remove('dark-mode');
    }
    updateDarkModeIcons(isDark);
  });
});

// Add settings button click handler
openSettingsBtn.addEventListener('click', () => {
  chrome.runtime.openOptionsPage();
});

// Add auto-restart checkbox change handler
autoRestartCheck.addEventListener('change', () => {
  // Only update the setting, don't restart the timer
  chrome.storage.local.set({ autoRestart: autoRestartCheck.checked });
  
  // Send the updated auto-restart setting to the background script
  chrome.runtime.sendMessage({
    action: 'updateAutoRestart',
    autoRestart: autoRestartCheck.checked
  });
});

// Add test chatbox button click handler
testChatboxBtn.addEventListener('click', () => {
  // Get the active tab and inject the chatbox
  chrome.tabs.query({active: true, currentWindow: true}, (tabs) => {
    if (tabs[0]) {
      chrome.tabs.sendMessage(tabs[0].id, {
        action: 'showTestChatbox',
        minutes: 25  // Default test duration
      });
    }
  });
}); 