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

// Dark mode toggle
darkModeToggle.addEventListener('click', () => {
  document.body.classList.toggle('dark-mode');
  document.body.classList.toggle('light-mode');
  
  // Update icon based on mode
  darkModeToggle.textContent = document.body.classList.contains('dark-mode') ? '☀️' : '🌙';

  const isDark = document.body.classList.contains('dark-mode');
  localStorage.setItem('darkMode', isDark ? 'enabled' : 'disabled');
});

// Initialize dark mode on load
document.addEventListener('DOMContentLoaded', () => {
  const mode = localStorage.getItem('darkMode');
  if (mode === 'enabled') {
    document.body.classList.add('dark-mode');
    document.body.classList.remove('light-mode');
    darkModeToggle.textContent = '☀️';
  } else {
    document.body.classList.add('light-mode');
    document.body.classList.remove('dark-mode');
    darkModeToggle.textContent = '🌙';
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

// Update display when time inputs change
[hoursInput, minutesInput, secondsInput].forEach(input => {
  input.addEventListener('change', () => {
    if (input.value < 0) input.value = 0;
    if (!isRunning) {
      timeLeft = calculateTotalSeconds();
      updateDisplay();
    }
  });
});

function calculateTotalSeconds() {
  const hours = parseInt(hoursInput.value) || 0;
  const minutes = parseInt(minutesInput.value) || 0;
  const seconds = parseInt(secondsInput.value) || 0;
  return (hours * 3600) + (minutes * 60) + seconds;
}

function startTimer() {
  if (!timeLeft || timeLeft <= 0) {
    timeLeft = calculateTotalSeconds();
  }
  
  isRunning = true;
  startStopBtn.textContent = 'Stop Timer';
  
  timer = setInterval(() => {
    timeLeft--;
    updateDisplay();
    
    // Show chatbox in last 30 seconds
    if (timeLeft === 30) {
      // Play sound
      playSound();
      
      // Show reflection chat box in the active tab
      chrome.tabs.query({active: true, currentWindow: true}, function(tabs) {
        if (!tabs || !tabs[0]) {
          console.error('No active tab found');
          return;
        }
        
        chrome.tabs.sendMessage(tabs[0].id, {
          action: 'showReflectionChatBox',
          minutes: 0.5, // 30 seconds = 0.5 minutes
          timeLeft: timeLeft
        }, (response) => {
          if (chrome.runtime.lastError) {
            console.error('Error sending message:', chrome.runtime.lastError);
          } else {
            console.log('Chatbox message sent successfully:', response);
          }
        });
      });
    }
    
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
  timeLeft = calculateTotalSeconds();
  updateDisplay();
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

function playSound() {
  const audio = new Audio(chrome.runtime.getURL('popup.mp3'));
  audio.play().catch(error => console.log('Error playing sound:', error));
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