let timer;
let timeLeft;
let isRunning = false;

// Initialize all UI elements
const startStopBtn = document.getElementById('startStop');
const resetBtn = document.getElementById('reset');
const minutesInput = document.getElementById('minutes');
const display = document.getElementById('display');
const autoRestartCheck = document.getElementById('autoRestart');
const darkModeToggle = document.getElementById('darkModeToggle');

// Dark mode toggle
darkModeToggle.addEventListener('click', () => {
  document.body.classList.toggle('dark-mode');
  document.body.classList.toggle('light-mode');

  const isDark = document.body.classList.contains('dark-mode');
  localStorage.setItem('darkMode', isDark ? 'enabled' : 'disabled');
});

// Initialize dark mode on load
document.addEventListener('DOMContentLoaded', () => {
  const mode = localStorage.getItem('darkMode');
  if (mode === 'enabled') {
    document.body.classList.add('dark-mode');
    document.body.classList.remove('light-mode');
  } else {
    document.body.classList.add('light-mode');
    document.body.classList.remove('dark-mode');
  }
});

// Timer controls
startStopBtn.addEventListener('click', () => {
  if (isRunning) {
    stopTimer();
  } else {
    startTimer();
  }
});

resetBtn.addEventListener('click', resetTimer);

// Update display when minutes input changes
minutesInput.addEventListener('change', () => {
  if (minutesInput.value < 1) minutesInput.value = 1;
  if (!isRunning) {
    timeLeft = minutesInput.value * 60;
    updateDisplay();
  }
});

function startTimer() {
  if (!timeLeft || timeLeft <= 0) {
    timeLeft = minutesInput.value * 60;
  }
  
  isRunning = true;
  startStopBtn.textContent = 'Stop Timer';
  
  timer = setInterval(() => {
    timeLeft--;
    updateDisplay();
    
    if (timeLeft <= 0) {
      timerComplete();
    }
  }, 1000);
}

function stopTimer() {
  clearInterval(timer);
  isRunning = false;
  startStopBtn.textContent = 'Start Timer';
}

function resetTimer() {
  stopTimer();
  timeLeft = minutesInput.value * 60;
  updateDisplay();
}

function updateDisplay() {
  const minutes = Math.floor(timeLeft / 60);
  const seconds = timeLeft % 60;
  display.textContent = `${minutes}:${seconds.toString().padStart(2, '0')}`;
}

function injectChatbox() {
  chrome.tabs.query({active: true, currentWindow: true}, function(tabs) {
    chrome.scripting.executeScript({
      target: { tabId: tabs[0].id },
      function: createChatbox,
      args: [minutesInput.value]
    });
  });
}

function createChatbox(minutes) {
  // Remove existing chatbox if any
  const existingChatbox = document.getElementById('timer-reflection-chatbox');
  if (existingChatbox) {
    existingChatbox.remove();
  }

  // Create and style chatbox
  const chatbox = document.createElement('div');
  chatbox.id = 'timer-reflection-chatbox';
  chatbox.style.cssText = `
    position: fixed;
    bottom: 20px;
    right: 20px;
    background: #2c2c2c;
    color: white;
    padding: 20px;
    border-radius: 10px;
    width: 300px;
    box-shadow: 0 0 10px rgba(0,0,0,0.4);
    z-index: 2147483647;
    font-family: Arial, sans-serif;
  `;

  chatbox.innerHTML = `
    <h3 style="margin: 0 0 10px 0;">Time's Up!</h3>
    <p style="margin: 0 0 10px 0;">What did you do in the last ${minutes} minutes?</p>
    <textarea style="width: 100%; min-height: 100px; margin-bottom: 10px; padding: 8px; box-sizing: border-box; border-radius: 5px;"></textarea>
    <button style="background: #4CAF50; color: white; border: none; padding: 8px 15px; border-radius: 4px; cursor: pointer; width: 100%;">Submit</button>
  `;

  document.body.appendChild(chatbox);

  // Handle submission
  const button = chatbox.querySelector('button');
  button.addEventListener('click', () => {
    const response = chatbox.querySelector('textarea').value;
    console.log('Timer reflection:', response);
    chatbox.remove();
  });

  // Handle Enter key in textarea
  const textarea = chatbox.querySelector('textarea');
  textarea.addEventListener('keypress', (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      button.click();
    }
  });

  // Focus textarea
  textarea.focus();
}

function timerComplete() {
  stopTimer();
  injectChatbox();
  
  if (autoRestartCheck.checked) {
    resetTimer();
    startTimer();
  } else {
    resetTimer();
  }
} 