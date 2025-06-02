// Create and play notification sound
async function playNotificationSound() {
  try {
    const audio = new Audio(chrome.runtime.getURL('popup.mp3'));
    await audio.play();
  } catch (error) {
    console.log('Error playing sound:', error);
  }
}

// Function to create and show the reflection chat box
function showReflectionChatBox(minutes) {
  console.log('Showing reflection chatbox with minutes:', minutes);
  
  // Remove existing chatbox if any
  const existingChatbox = document.getElementById('reflection-chatbox');
  if (existingChatbox) {
    console.log('Removing existing chatbox');
    existingChatbox.remove();
  }

  // Create chatbox container
  const chatbox = document.createElement('div');
  chatbox.id = 'reflection-chatbox';
  
  // Format the time frame message
  const timeFrameMsg = minutes < 1 ? '30 seconds' : `${Math.round(minutes)} minutes`;
  console.log('Using time frame message:', timeFrameMsg);
  
  // Create chatbox content
  chatbox.innerHTML = `
    <div class="chatbox-header">Time Check!</div>
    <div class="chatbox-body">
      <p>What did you do in the last ${timeFrameMsg}?</p>
      <textarea placeholder="Type your response here..."></textarea>
      <div class="button-container">
        <button id="skip-reflection">Skip</button>
        <button id="submit-reflection">Submit</button>
      </div>
    </div>
  `;

  // Ensure the chatbox is added to the top-level document
  document.documentElement.appendChild(chatbox);
  console.log('Chatbox added to document');

  // Play notification sound
  playNotificationSound();

  // Focus textarea after a short delay to ensure proper focus
  const textarea = chatbox.querySelector('textarea');
  setTimeout(() => {
    textarea.focus();
  }, 300);

  // Handle submit button click
  const submitBtn = chatbox.querySelector('#submit-reflection');
  submitBtn.addEventListener('click', () => {
    const reflection = textarea.value.trim();
    if (reflection) {
      // Store reflection in chrome.storage
      chrome.storage.local.get(['reflections'], (result) => {
        const reflections = result.reflections || [];
        reflections.push({
          timestamp: new Date().toISOString(),
          duration: minutes,
          reflection: reflection
        });
        chrome.storage.local.set({ reflections });
      });
    }
    chatbox.remove();
  });

  // Handle skip button click
  const skipBtn = chatbox.querySelector('#skip-reflection');
  skipBtn.addEventListener('click', () => {
    chatbox.remove();
  });

  // Handle Enter key in textarea
  textarea.addEventListener('keypress', (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      submitBtn.click();
    }
  });

  // Handle Escape key to close
  document.addEventListener('keydown', function(e) {
    if (e.key === 'Escape') {
      chatbox.remove();
    }
  });
}

// Listen for messages from the popup
chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
  console.log('Received message:', request);
  if (request.action === 'showReflectionChatBox') {
    showReflectionChatBox(request.minutes);
    sendResponse({ success: true });
    return true;
  }
}); 