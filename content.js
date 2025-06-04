// Log that content script has loaded
console.log('[Content] Timer extension content script loaded');

// Prevent multiple injections
if (!window.timerExtensionInitialized) {
  window.timerExtensionInitialized = true;

  // Initialize volume if not already initialized
  let sfxVolume = 0.5; // Default to 50%
  // Load saved volume
  chrome.storage.local.get(['sfxVolume'], (result) => {
    if (result.sfxVolume !== undefined) {
      sfxVolume = result.sfxVolume / 100;
      console.log('[Content] Loaded saved volume:', sfxVolume);
    }
  });

  // Add theme change listener
  const darkModeMediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
  darkModeMediaQuery.addListener((e) => {
    const chatbox = document.getElementById('reflection-chatbox');
    if (chatbox) {
      // Only update if there's no manual override
      if (localStorage.getItem('darkMode') === null) {
        updateChatboxTheme(e.matches);
      }
    }
  });

  // Create and play notification sound
  async function playNotificationSound() {
    try {
      const audioUrl = chrome.runtime.getURL('popup.mp3');
      console.log('[Content] Loading audio from URL:', audioUrl);
      
      const audio = new Audio(audioUrl);
      audio.volume = Math.max(0, Math.min(1, sfxVolume));
      console.log('[Content] Created audio element with volume:', audio.volume);
      
      // Add error event listener
      audio.onerror = (e) => {
        console.error('[Content] Audio error:', e);
      };
      
      // Add loadeddata event listener
      audio.onloadeddata = () => {
        console.log('[Content] Audio loaded successfully');
      };
      
      // Wait for the audio to be loaded
      await new Promise((resolve, reject) => {
        audio.oncanplaythrough = resolve;
        audio.onerror = reject;
        audio.load();
      });
      
      console.log('[Content] About to play sound with volume:', audio.volume);
      await audio.play();
      console.log('[Content] Notification sound played successfully');
    } catch (error) {
      console.error('[Content] Error playing sound:', error);
    }
  }

  // Debug function to check if element is actually visible
  function isElementVisible(element) {
    return element && 
           window.getComputedStyle(element).display !== 'none' &&
           window.getComputedStyle(element).visibility !== 'hidden';
  }

  // Function to create and show the reflection chat box
  function showReflectionChatBox(minutes) {
    console.log('[Content] Attempting to show reflection chatbox with minutes:', minutes);
    
    try {
      // Check if chatbox already exists
      const existingChatbox = document.getElementById('reflection-chatbox');
      if (existingChatbox) {
        console.log('[Content] Chatbox already exists, not creating another one');
        return true; // Return true to indicate success (chatbox is already shown)
      }

      // Play notification sound first
      playNotificationSound().then(() => {
        console.log('[Content] Sound played, now showing chatbox');
      }).catch(error => {
        console.error('[Content] Error playing sound:', error);
      });

      // Get dark mode state from chrome.storage.local
      chrome.storage.local.get(['darkMode'], (darkModeResult) => {
        // Check system color scheme preference
        const prefersDarkMode = window.matchMedia('(prefers-color-scheme: dark)').matches;
        // Use stored preference or system preference as fallback
        const isDarkMode = darkModeResult.darkMode === 'enabled' || (darkModeResult.darkMode === undefined && prefersDarkMode);
        const bgColor = isDarkMode ? 'rgba(32, 33, 35, 0.95)' : 'rgba(255, 255, 255, 0.95)';
        const textColor = isDarkMode ? '#ffffff' : '#333333';
        const inputBgColor = isDarkMode ? 'rgba(64, 65, 79, 0.9)' : 'rgba(240, 240, 240, 0.9)';
        const buttonBgColor = isDarkMode ? 'rgba(255, 255, 255, 0.1)' : 'rgba(0, 0, 0, 0.1)';

        // Create chatbox container with updated styles
        const chatbox = document.createElement('div');
        chatbox.id = 'reflection-chatbox';
        chatbox.style.cssText = `
          position: fixed;
          bottom: 20px;
          right: 20px;
          width: 400px;
          background: ${bgColor};
          border-radius: 24px;
          box-shadow: 0 4px 30px rgba(0, 0, 0, 0.4);
          font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Arial, sans-serif;
          z-index: 2147483647;
          display: block !important;
          visibility: visible !important;
          opacity: 1 !important;
          pointer-events: auto !important;
          backdrop-filter: blur(10px);
          -webkit-backdrop-filter: blur(10px);
          color: ${textColor};
          padding: 32px;
          animation: slideIn 0.3s ease-out;
        `;
        
        // Create chatbox content with updated design
        chatbox.innerHTML = `
          <style>
            @keyframes slideIn {
              from {
                transform: translateY(20px);
                opacity: 0;
              }
              to {
                transform: translateY(0);
                opacity: 1;
              }
            }
            #reflection-chatbox textarea {
              width: 100%;
              min-height: 80px;
              padding: 16px;
              border-radius: 16px;
              background: ${inputBgColor};
              border: none;
              color: ${textColor};
              font-size: 16px;
              resize: none;
              margin: 20px 0;
              font-family: inherit;
              outline: none;
              z-index: 2147483647;
              position: relative;
            }
            #reflection-chatbox textarea:focus {
              box-shadow: 0 0 0 2px rgba(128, 128, 128, 0.2);
            }
            #reflection-chatbox textarea::placeholder {
              color: ${isDarkMode ? 'rgba(255, 255, 255, 0.5)' : 'rgba(0, 0, 0, 0.5)'};
            }
          </style>
          <div style="font-size: 32px; font-weight: 500; margin-bottom: 24px; color: ${textColor}">Time is Up!</div>
          <div style="font-size: 24px; color: ${textColor}">How did you spend your time?</div>
          <textarea id="reflection-textarea" placeholder="Type your response here..." tabindex="1"></textarea>
          <div style="display: flex; gap: 12px; justify-content: flex-end;">
            <button id="skip-reflection" style="background: ${buttonBgColor}; color: ${textColor}; border: none; padding: 12px 24px; border-radius: 12px; cursor: pointer; font-size: 16px; transition: all 0.2s;">Skip</button>
            <button id="submit-reflection" style="background: #4CAF50; color: white; border: none; padding: 12px 24px; border-radius: 12px; cursor: pointer; font-size: 16px; transition: all 0.2s;">Submit</button>
          </div>
        `;

        // Ensure the chatbox is added to the top-level document
        if (document.body) {
          document.body.appendChild(chatbox);
          console.log('[Content] Chatbox added to document body');
          
          // Focus the textarea
          const textarea = chatbox.querySelector('#reflection-textarea');
          if (textarea) {
            // Initial focus
            textarea.focus();
            console.log('[Content] Initial focus set on textarea');
          }
        } else if (document.documentElement) {
          document.documentElement.appendChild(chatbox);
          console.log('[Content] Chatbox added to document root');
          
          // Focus the textarea
          const textarea = chatbox.querySelector('#reflection-textarea');
          if (textarea) {
            // Initial focus
            textarea.focus();
            console.log('[Content] Initial focus set on textarea');
          }
        } else {
          throw new Error('Could not find document body or root element');
        }

        // Handle submit button click
        const submitBtn = chatbox.querySelector('#submit-reflection');
        const textarea = chatbox.querySelector('#reflection-textarea');
        
        if (submitBtn && textarea) {
          submitBtn.addEventListener('click', async () => {
            const userResponse = textarea.value.trim();
            if (userResponse) {
              try {
                // Show loading state
                submitBtn.disabled = true;
                submitBtn.textContent = 'Saving...';
                
                // Get credentials from storage
                const credentials = await new Promise((resolve) => {
                  chrome.storage.local.get([
                    'GOOGLE_SHEETS_CLIENT_EMAIL',
                    'GOOGLE_SHEETS_PRIVATE_KEY',
                    'SPREADSHEET_ID'
                  ], (result) => {
                    if (!result.GOOGLE_SHEETS_CLIENT_EMAIL || !result.GOOGLE_SHEETS_PRIVATE_KEY || !result.SPREADSHEET_ID) {
                      throw new Error('Missing Google Sheets credentials. Please check the extension settings.');
                    }
                    resolve({
                      clientEmail: result.GOOGLE_SHEETS_CLIENT_EMAIL,
                      privateKey: result.GOOGLE_SHEETS_PRIVATE_KEY,
                      spreadsheetId: result.SPREADSHEET_ID
                    });
                  });
                });

                // Update Google Sheet directly
                await updateGoogleSheet(credentials, userResponse);
                
                // Show success message
                const successMsg = document.createElement('div');
                successMsg.textContent = 'Reflection saved successfully!';
                successMsg.style.cssText = `
                  color: #4CAF50;
                  margin-bottom: 10px;
                  text-align: center;
                  font-weight: bold;
                `;
                submitBtn.parentElement.insertBefore(successMsg, submitBtn);
                
                // Remove chatbox after a short delay
                setTimeout(() => {
                  chatbox.remove();
                }, 1500);
              } catch (error) {
                console.error('[Content] Error sending to Google Sheets:', error);
                submitBtn.disabled = false;
                submitBtn.textContent = 'Submit';
                
                // Show error message
                const errorMsg = document.createElement('div');
                errorMsg.textContent = error.message.includes('credentials') ? 
                  'Please check Google Sheets settings in extension options' : 
                  'Error saving message. Please try again.';
                errorMsg.style.cssText = `
                  color: #f44336;
                  margin-bottom: 10px;
                  text-align: center;
                  font-weight: bold;
                `;
                submitBtn.parentElement.insertBefore(errorMsg, submitBtn);
                
                // Remove error message after 3 seconds
                setTimeout(() => {
                  errorMsg.remove();
                }, 3000);
              }
            }
          });
        }

        // Handle skip button click
        const skipBtn = chatbox.querySelector('#skip-reflection');
        if (skipBtn) {
          skipBtn.addEventListener('click', () => {
            chatbox.remove();
            console.log('[Content] Chatbox skipped and removed');
          });
        }

        // Handle Enter key in textarea
        if (textarea) {
          textarea.addEventListener('keypress', (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
              e.preventDefault();
              submitBtn.click();
              console.log('[Content] Reflection submitted via Enter key');
            }
          });
        }

        // Handle Escape key to close
        const escapeHandler = function(e) {
          if (e.key === 'Escape') {
            chatbox.remove();
            document.removeEventListener('keydown', escapeHandler);
            console.log('[Content] Chatbox closed via Escape key');
          }
        };
        document.addEventListener('keydown', escapeHandler);
      });

      console.log('[Content] Chatbox setup completed successfully');
      return true;
    } catch (error) {
      console.error('[Content] Error showing reflection chatbox:', error);
      return false;
    }
  }

  // Function to update chatbox theme
  function updateChatboxTheme(isDark) {
    const chatbox = document.getElementById('reflection-chatbox');
    if (!chatbox) return;

    const bgColor = isDark ? 'rgba(32, 33, 35, 0.95)' : 'rgba(255, 255, 255, 0.95)';
    const textColor = isDark ? '#ffffff' : '#333333';
    const inputBgColor = isDark ? 'rgba(64, 65, 79, 0.9)' : 'rgba(240, 240, 240, 0.9)';
    const buttonBgColor = isDark ? 'rgba(255, 255, 255, 0.1)' : 'rgba(0, 0, 0, 0.1)';

    chatbox.style.background = bgColor;
    chatbox.style.color = textColor;

    const textarea = chatbox.querySelector('textarea');
    if (textarea) {
      textarea.style.background = inputBgColor;
      textarea.style.color = textColor;
    }

    const headers = chatbox.querySelectorAll('div[style*="font-size"]');
    headers.forEach(header => {
      header.style.color = textColor;
    });

    const skipButton = chatbox.querySelector('#skip-reflection');
    if (skipButton) {
      skipButton.style.background = buttonBgColor;
      skipButton.style.color = textColor;
    }
  }

  // Function to test if the content script is working
  function testContentScript() {
    console.log('[Content] Content script test function called');
    // Create a test element to verify script is running
    const testElement = document.createElement('div');
    testElement.style.cssText = `
      position: fixed;
      top: 10px;
      right: 10px;
      background: red;
      color: white;
      padding: 10px;
      z-index: 2147483647;
      font-family: Arial;
    `;
    testElement.textContent = 'Content Script Test - Click to show chatbox';
    testElement.addEventListener('click', () => showReflectionChatBox(0.5));
    document.body.appendChild(testElement);
    console.log('[Content] Test element added to page');
    
    // Also try to show the chatbox directly
    showReflectionChatBox(0.5);
  }

  // Listen for messages from the popup
  chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
    console.log('[Content] Received message:', request);
    try {
      if (request.action === 'showReflectionChatBox') {
        const success = showReflectionChatBox(request.minutes);
        console.log('[Content] Showing chatbox result:', success);
        sendResponse({ success: success });
        return true;
      } else if (request.action === 'testContentScript') {
        testContentScript();
        console.log('[Content] Test function executed');
        sendResponse({ success: true });
        return true;
      } else if (request.action === 'updateVolume') {
        sfxVolume = request.volume;
        console.log('[Content] Volume updated to:', sfxVolume);
        sendResponse({ success: true });
        return true;
      } else if (request.action === 'testSound') {
        sfxVolume = request.volume;
        console.log('[Content] Testing sound with volume:', sfxVolume);
        playNotificationSound();
        sendResponse({ success: true });
        return true;
      } else if (request.action === 'themeChanged') {
        console.log('[Content] Theme changed, isDark:', request.isDark);
        updateChatboxTheme(request.isDark);
        sendResponse({ success: true });
        return true;
      }
    } catch (error) {
      console.error('[Content] Error handling message:', error);
      sendResponse({ success: false, error: error.message });
      return true;
    }
  });

  // Listen for messages from the extension
  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message.action === 'showTestChatbox') {
      showReflectionChatBox(message.minutes);
      sendResponse({ success: true });
      return true;
    }
  });

  // Add global hotkey handler for Alt+Enter
  document.addEventListener('keydown', (e) => {
    // Check if Alt+Enter was pressed
    if (e.altKey && e.key === 'Enter') {
      const chatbox = document.getElementById('reflection-chatbox');
      if (chatbox) {
        const textarea = chatbox.querySelector('textarea');
        if (textarea) {
          e.preventDefault(); // Prevent default Alt+Enter behavior
          textarea.focus();
          console.log('[Content] Focused chatbox textarea via Alt+Enter hotkey');
        }
      }
    }
  });

  // Log that content script has finished loading
  console.log('[Content] Timer extension content script initialization complete');
}

function handleSubmit(event) {
  event.preventDefault();
  const textArea = document.querySelector('.reflection-chatbox textarea');
  if (!textArea) return;

  const message = textArea.value.trim();
  if (!message) return;

  console.log('Submitting reflection:', message);

  // Send message to background script to update Google Sheet
  chrome.runtime.sendMessage({
    action: 'updateGoogleSheet',
    message: message
  }, (response) => {
    if (response && response.success) {
      console.log('Successfully wrote reflection to Google Sheet:', response);
      // Clear and close the chatbox
      textArea.value = '';
      const chatbox = document.querySelector('.reflection-chatbox');
      if (chatbox) {
        chatbox.remove();
      }
    } else {
      console.error('Failed to write to Google Sheet:', response?.error || 'Unknown error');
      alert('Failed to save your reflection. Please try again.');
    }
  });
}

function createReflectionChatBox(minutes, timeLeft, sheetsLink) {
  // Remove any existing chatbox
  const existingChatbox = document.querySelector('.reflection-chatbox');
  if (existingChatbox) {
    existingChatbox.remove();
  }

  // Create chatbox container
  const chatbox = document.createElement('div');
  chatbox.className = 'reflection-chatbox';
  chatbox.style.cssText = `
    position: fixed;
    bottom: 20px;
    right: 20px;
    width: 300px;
    background: white;
    border: 1px solid #ccc;
    border-radius: 8px;
    padding: 15px;
    box-shadow: 0 2px 10px rgba(0,0,0,0.1);
    z-index: 10000;
  `;

  // Create form
  const form = document.createElement('form');
  form.addEventListener('submit', handleSubmit);

  // Add textarea
  const textarea = document.createElement('textarea');
  textarea.style.cssText = `
    width: 100%;
    height: 100px;
    margin-bottom: 10px;
    padding: 8px;
    border: 1px solid #ddd;
    border-radius: 4px;
    resize: vertical;
  `;
  textarea.placeholder = 'What have you accomplished in this session?';

  // Add submit button
  const submitBtn = document.createElement('button');
  submitBtn.textContent = 'Submit Reflection';
  submitBtn.style.cssText = `
    background: #4CAF50;
    color: white;
    padding: 8px 16px;
    border: none;
    border-radius: 4px;
    cursor: pointer;
  `;
  submitBtn.type = 'submit';

  // Add close button
  const closeBtn = document.createElement('button');
  closeBtn.textContent = '×';
  closeBtn.style.cssText = `
    position: absolute;
    top: 5px;
    right: 5px;
    background: none;
    border: none;
    font-size: 20px;
    cursor: pointer;
    color: #666;
  `;
  closeBtn.addEventListener('click', () => chatbox.remove());

  // Assemble the chatbox
  form.appendChild(textarea);
  form.appendChild(submitBtn);
  chatbox.appendChild(closeBtn);
  chatbox.appendChild(form);
  document.body.appendChild(chatbox);

  // Apply dark mode if needed
  chrome.storage.local.get(['darkMode'], (result) => {
    if (result.darkMode === 'enabled') {
      applyDarkMode(chatbox);
    }
  });

  return true;
}

// Function to base64 encode string safely
function base64UrlEncode(str) {
  const encoded = encodeURIComponent(str).replace(/%([0-9A-F]{2})/g,
    function toChar(match, p1) {
      return String.fromCharCode('0x' + p1);
    });
  return window.btoa(encoded).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
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
    // Clean and format the private key properly
    const cleanPrivateKey = privateKey
      .replace(/\\n/g, '\n')  // Convert literal \n to actual newlines
      .replace(/"/g, '')      // Remove any quotes
      .replace(/^\s+|\s+$/g, ''); // Trim whitespace

    // Ensure the key has proper PEM format
    const pemHeader = '-----BEGIN PRIVATE KEY-----\n';
    const pemFooter = '\n-----END PRIVATE KEY-----';
    
    // Add PEM header/footer if not present
    const formattedKey = cleanPrivateKey.includes(pemHeader) ? 
      cleanPrivateKey : 
      `${pemHeader}${cleanPrivateKey}${pemFooter}`;

    // Extract the base64 part between header and footer
    const base64Key = formattedKey
      .replace(pemHeader, '')
      .replace(pemFooter, '')
      .replace(/\s/g, ''); // Remove all whitespace

    try {
      // Decode the base64 key
      const binaryDer = new Uint8Array(
        [...window.atob(base64Key)].map(char => char.charCodeAt(0))
      );

      const cryptoKey = await window.crypto.subtle.importKey(
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
      const signatureBuffer = await window.crypto.subtle.sign(
        'RSASSA-PKCS1-v1_5',
        cryptoKey,
        encoder.encode(input)
      );

      // Convert signature to base64url
      return window.btoa(String.fromCharCode(...new Uint8Array(signatureBuffer)))
        .replace(/\+/g, '-')
        .replace(/\//g, '_')
        .replace(/=+$/, '');
    } catch (decodeError) {
      console.error('Base64 decode error:', decodeError);
      console.log('Attempted to decode key:', base64Key);
      throw new Error('Invalid private key format: ' + decodeError.message);
    }
  } catch (error) {
    console.error('Error signing JWT:', error);
    throw error;
  }
}

// Function to validate credentials
function validateCredentials(credentials) {
  const { clientEmail, privateKey, spreadsheetId } = credentials;
  
  if (!clientEmail || !privateKey || !spreadsheetId) {
    throw new Error('Missing required credentials');
  }

  // Validate client email format
  if (!clientEmail.includes('@') || !clientEmail.includes('.iam.gserviceaccount.com')) {
    throw new Error('Invalid service account email format');
  }

  // Validate private key basic format
  if (!privateKey.includes('PRIVATE KEY')) {
    throw new Error('Private key missing required markers');
  }

  // Validate spreadsheet ID format (basic check)
  if (!/^[a-zA-Z0-9-_]+$/.test(spreadsheetId)) {
    throw new Error('Invalid spreadsheet ID format');
  }

  return true;
}

// Function to get formatted time (e.g., "9:00 PM")
function getFormattedTime() {
  const now = new Date();
  let hours = now.getHours();
  const minutes = String(now.getMinutes()).padStart(2, '0');
  const ampm = hours >= 12 ? 'PM' : 'AM';
  
  // Convert to 12-hour format
  hours = hours % 12;
  hours = hours ? hours : 12; // Convert 0 to 12
  
  return `${hours}:${minutes} ${ampm}`;
}

// Function to convert column number to letter (1 = A, 2 = B, etc.)
function getColumnLetter(columnNumber) {
  let letter = '';
  while (columnNumber > 0) {
    const remainder = (columnNumber - 1) % 26;
    letter = String.fromCharCode(65 + remainder) + letter;
    columnNumber = Math.floor((columnNumber - 1) / 26);
  }
  return letter;
}

// Function to find first empty cell considering column shifts
async function findFirstEmptyCell(token, spreadsheetId) {
  try {
    // Start with first column pair (A and B)
    let columnPairIndex = 0; // 0 = A&B, 1 = C&D, 2 = E&F, etc.
    let startColumn = getColumnLetter(1 + (columnPairIndex * 2)); // A, C, E, etc.
    let endColumn = getColumnLetter(2 + (columnPairIndex * 2)); // B, D, F, etc.
    
    while (true) {
      // Check current column pair
      const response = await fetch(
        `https://sheets.googleapis.com/v4/spreadsheets/${spreadsheetId}/values/${startColumn}1:${startColumn}16`,
        {
          method: 'GET',
          headers: {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'application/json',
          }
        }
      );

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      const data = await response.json();
      const values = data.values || [];
      
      // Find first empty row in current column
      let row = 1;
      for (let i = 0; i < values.length; i++) {
        if (!values[i] || !values[i][0] || values[i][0].trim() === '') {
          row = i + 1;
          return {
            row,
            timestampColumn: startColumn,
            messageColumn: endColumn
          };
        }
      }
      
      // If we've checked all 16 rows and they're full
      if (values.length >= 16) {
        // Move to next column pair
        columnPairIndex++;
        startColumn = getColumnLetter(1 + (columnPairIndex * 2));
        endColumn = getColumnLetter(2 + (columnPairIndex * 2));
        // Start checking from row 1 in new columns
        continue;
      }
      
      // If we have less than 16 rows, use the next available row
      row = values.length + 1;
      return {
        row,
        timestampColumn: startColumn,
        messageColumn: endColumn
      };
    }
  } catch (error) {
    console.error('Error finding first empty cell:', error);
    throw error;
  }
}

// Function to update Google Sheet
async function updateGoogleSheet(credentials, message) {
  try {
    // Validate credentials first
    validateCredentials(credentials);
    
    const { clientEmail, privateKey, spreadsheetId } = credentials;
    
    // Get access token
    const token = await getAccessToken(clientEmail, privateKey);
    
    // Get current time
    const timestamp = getFormattedTime();

    // Find first empty cell position
    const targetCell = await findFirstEmptyCell(token, spreadsheetId);
    console.log('Inserting at position:', targetCell);
    
    // Update cells in the target position
    const range = `${targetCell.timestampColumn}${targetCell.row}:${targetCell.messageColumn}${targetCell.row}`;
    const response = await fetch(
      `https://sheets.googleapis.com/v4/spreadsheets/${spreadsheetId}/values/${range}?valueInputOption=RAW`,
      {
        method: 'PUT',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          values: [[timestamp, message]]
        })
      }
    );

    if (!response.ok) {
      const errorText = await response.text();
      throw new Error(`HTTP error! status: ${response.status}, body: ${errorText}`);
    }

    const data = await response.json();
    console.log(`Cells ${range} updated successfully:`, data);
    return data;
  } catch (error) {
    console.error('Error updating Google Sheet:', error);
    throw error;
  }
} 