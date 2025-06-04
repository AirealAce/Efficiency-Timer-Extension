require('dotenv').config();
const { google } = require('googleapis');

// Validate environment variables
function validateEnvironmentVars() {
  const required = ['GOOGLE_SHEETS_CLIENT_EMAIL', 'GOOGLE_SHEETS_PRIVATE_KEY', 'SPREADSHEET_ID'];
  const missing = required.filter(key => !process.env[key]);
  
  if (missing.length > 0) {
    throw new Error(`Missing required environment variables: ${missing.join(', ')}`);
  }
}

// Configure Google Sheets API
async function getAuthClient() {
  try {
    validateEnvironmentVars();
    
    return new google.auth.GoogleAuth({
      credentials: {
        client_email: process.env.GOOGLE_SHEETS_CLIENT_EMAIL,
        private_key: process.env.GOOGLE_SHEETS_PRIVATE_KEY.replace(/\\n/g, '\n'),
      },
      scopes: ['https://www.googleapis.com/auth/spreadsheets'],
    });
  } catch (error) {
    console.error('Error configuring auth client:', error);
    throw error;
  }
}

// Simple test function to write "Testing" to cell A1
async function updateA1(credentials) {
  try {
    const { clientEmail, privateKey, spreadsheetId, message = 'Testing' } = credentials;
    
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
    console.error('Error updating cell A1:', error);
    throw new Error(`Failed to update Google Sheet: ${error.message}`);
  }
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
    const headerBase64 = btoa(JSON.stringify(header));
    const claimBase64 = btoa(JSON.stringify(claim));
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

// Function to sign JWT using Web Crypto API
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
    
    const binaryKey = Uint8Array.from(atob(pemContents), c => c.charCodeAt(0));
    const cryptoKey = await crypto.subtle.importKey(
      'pkcs8',
      binaryKey,
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

    // Convert signature to base64
    const signatureArray = Array.from(new Uint8Array(signatureBuffer));
    const signatureBase64 = btoa(String.fromCharCode.apply(null, signatureArray));
    
    return signatureBase64;
  } catch (error) {
    console.error('Error signing JWT:', error);
    throw error;
  }
}

// Function to append chat message to Google Sheet
async function appendChatMessage(message) {
  try {
    const auth = await getAuthClient();
    const sheets = google.sheets({ version: 'v4', auth });
    
    const timestamp = new Date().toLocaleString();
    const response = await sheets.spreadsheets.values.append({
      spreadsheetId: process.env.SPREADSHEET_ID,
      range: 'Template!A:B',
      valueInputOption: 'RAW',
      insertDataOption: 'INSERT_ROWS',
      resource: {
        values: [[timestamp, message]]
      }
    });

    console.log('Message appended:', response.data);
    return response.data;
  } catch (error) {
    console.error('Error appending message:', error);
    throw error;
  }
}

// Make functions available globally if in browser environment
if (typeof window !== 'undefined') {
  window.sheetsApi = {
    updateA1,
    getAccessToken,
    signJWT
  };
}

module.exports = {
  updateA1,
  appendChatMessage
}; 