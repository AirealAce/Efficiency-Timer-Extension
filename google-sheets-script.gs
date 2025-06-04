function doGet(e) {
  const headers = {
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
    'Access-Control-Allow-Headers': 'Content-Type, Accept',
    'Access-Control-Max-Age': '86400'
  };
  
  return ContentService.createTextOutput('Script is running')
    .setMimeType(ContentService.MimeType.TEXT)
    .setHeaders(headers);
}

function doOptions(e) {
  const headers = {
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
    'Access-Control-Allow-Headers': 'Content-Type, Accept',
    'Access-Control-Max-Age': '86400'
  };
  
  return ContentService.createTextOutput('')
    .setMimeType(ContentService.MimeType.TEXT)
    .setHeaders(headers);
}

function doPost(e) {
  const headers = {
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
    'Access-Control-Allow-Headers': 'Content-Type, Accept',
    'Access-Control-Max-Age': '86400'
  };
  
  try {
    // Parse the incoming data
    const data = JSON.parse(e.postData.contents);
    const sheetUrl = data.sheetUrl;
    const message = data.message;
    
    // Extract the spreadsheet ID from the URL
    const regex = /\/d\/(.*?)\/edit/;
    const match = sheetUrl.match(regex);
    if (!match) {
      return ContentService.createTextOutput(JSON.stringify({
        success: false,
        error: 'Invalid Google Sheets URL'
      }))
      .setMimeType(ContentService.MimeType.JSON)
      .setHeaders(headers);
    }
    
    const spreadsheetId = match[1];
    
    // Open the spreadsheet and get the first sheet
    const spreadsheet = SpreadsheetApp.openById(spreadsheetId);
    const sheet = spreadsheet.getSheets()[0];
    
    // Write the message to cell A1
    sheet.getRange('A1').setValue(message);
    
    return ContentService.createTextOutput(JSON.stringify({
      success: true,
      message: 'Successfully updated Google Sheet'
    }))
    .setMimeType(ContentService.MimeType.JSON)
    .setHeaders(headers);
    
  } catch (error) {
    return ContentService.createTextOutput(JSON.stringify({
      success: false,
      error: 'Error saving to Google Sheet: ' + error.toString()
    }))
    .setMimeType(ContentService.MimeType.JSON)
    .setHeaders(headers);
  }
} 