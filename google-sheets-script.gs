/**
 * Reflection Timer receiver.
 *
 * Required Script Properties:
 *   SPREADSHEET_ID       The one spreadsheet this deployment may update.
 *   REFLECTION_API_TOKEN A long random token shared with the extension.
 *
 * Deploy as a web app that executes as you. The extension posts text/plain so
 * the request works cleanly from a Manifest V3 service worker.
 */

const APP_VERSION = '2.0.0';
const ROWS_PER_BLOCK = 16;
const MAX_COLUMN_PAIRS = 100;
const MAX_REFLECTION_LENGTH = 5000;

function doGet() {
  return jsonOutput_({
    success: true,
    service: 'Reflection Timer',
    version: APP_VERSION
  });
}

function doPost(event) {
  let lock;
  try {
    if (!event || !event.postData || !event.postData.contents) {
      throw new Error('The request body is empty.');
    }

    const payload = JSON.parse(event.postData.contents);
    const config = getConfig_();
    requireValidToken_(payload.token, config.apiToken);

    const requestedSpreadsheetId = extractSpreadsheetId_(payload.sheetUrl);
    if (!requestedSpreadsheetId || requestedSpreadsheetId !== config.spreadsheetId) {
      throw new Error('The requested spreadsheet does not match SPREADSHEET_ID.');
    }

    const sheetName = String(payload.sheetName || 'Template').trim();
    if (!sheetName || sheetName.length > 100) {
      throw new Error('The target tab name is invalid.');
    }

    const spreadsheet = SpreadsheetApp.openById(config.spreadsheetId);
    const sheet = spreadsheet.getSheetByName(sheetName);
    if (!sheet) {
      throw new Error(`The sheet tab "${sheetName}" does not exist.`);
    }

    if (payload.action === 'ping') {
      return jsonOutput_({
        success: true,
        version: APP_VERSION,
        target: `${spreadsheet.getName()} / ${sheet.getName()}`
      });
    }

    if (payload.action !== 'appendReflection') {
      throw new Error('Unsupported action.');
    }

    const message = String(payload.message || '').trim();
    if (!message) {
      throw new Error('The reflection is empty.');
    }
    if (message.length > MAX_REFLECTION_LENGTH) {
      throw new Error(`The reflection exceeds ${MAX_REFLECTION_LENGTH} characters.`);
    }

    lock = LockService.getScriptLock();
    lock.waitLock(15000);
    const result = appendReflection_(sheet, message);
    SpreadsheetApp.flush();

    return jsonOutput_({
      success: true,
      version: APP_VERSION,
      sheet: sheet.getName(),
      range: result.range,
      timestamp: result.timestamp.toISOString()
    });
  } catch (error) {
    console.error(error);
    return jsonOutput_({
      success: false,
      error: error && error.message ? error.message : String(error)
    });
  } finally {
    if (lock && lock.hasLock()) {
      lock.releaseLock();
    }
  }
}

function getConfig_() {
  const properties = PropertiesService.getScriptProperties();
  const spreadsheetId = String(properties.getProperty('SPREADSHEET_ID') || '').trim();
  const apiToken = String(properties.getProperty('REFLECTION_API_TOKEN') || '').trim();

  if (!/^[A-Za-z0-9_-]{20,}$/.test(spreadsheetId)) {
    throw new Error('SPREADSHEET_ID is missing or invalid in Script Properties.');
  }
  if (apiToken.length < 16) {
    throw new Error('REFLECTION_API_TOKEN is missing or too short in Script Properties.');
  }
  return { spreadsheetId, apiToken };
}

function requireValidToken_(providedToken, expectedToken) {
  const providedDigest = digest_(String(providedToken || ''));
  const expectedDigest = digest_(expectedToken);
  if (!constantTimeEqual_(providedDigest, expectedDigest)) {
    throw new Error('Unauthorized request.');
  }
}

function digest_(value) {
  return Utilities.computeDigest(
    Utilities.DigestAlgorithm.SHA_256,
    value,
    Utilities.Charset.UTF_8
  );
}

function constantTimeEqual_(left, right) {
  if (!left || !right || left.length !== right.length) {
    return false;
  }
  let difference = 0;
  for (let index = 0; index < left.length; index += 1) {
    difference |= left[index] ^ right[index];
  }
  return difference === 0;
}

function extractSpreadsheetId_(value) {
  const input = String(value || '').trim();
  if (/^[A-Za-z0-9_-]{20,}$/.test(input)) {
    return input;
  }
  const match = input.match(/\/spreadsheets\/d\/([A-Za-z0-9_-]{20,})/);
  return match ? match[1] : null;
}

function appendReflection_(sheet, message) {
  for (let pairIndex = 0; pairIndex < MAX_COLUMN_PAIRS; pairIndex += 1) {
    const timestampColumn = 1 + (pairIndex * 2);
    ensureColumns_(sheet, timestampColumn + 1);
    const block = sheet.getRange(1, timestampColumn, ROWS_PER_BLOCK, 2);
    const values = block.getDisplayValues();

    for (let rowIndex = 0; rowIndex < ROWS_PER_BLOCK; rowIndex += 1) {
      const timestampIsEmpty = String(values[rowIndex][0] || '').trim() === '';
      const messageIsEmpty = String(values[rowIndex][1] || '').trim() === '';
      if (timestampIsEmpty && messageIsEmpty) {
        const timestamp = new Date();
        const target = sheet.getRange(rowIndex + 1, timestampColumn, 1, 2);
        target.setValues([[timestamp, message]]);
        target.getCell(1, 1).setNumberFormat('h:mm AM/PM');
        target.getCell(1, 2).setWrap(true);
        return { range: target.getA1Notation(), timestamp };
      }
    }
  }
  throw new Error('No open reflection slot was found.');
}

function ensureColumns_(sheet, requiredColumns) {
  const missingColumns = requiredColumns - sheet.getMaxColumns();
  if (missingColumns > 0) {
    sheet.insertColumnsAfter(sheet.getMaxColumns(), missingColumns);
  }
}

function jsonOutput_(value) {
  return ContentService
    .createTextOutput(JSON.stringify(value))
    .setMimeType(ContentService.MimeType.JSON);
}
