/**
 * Reflection Timer receiver.
 *
 * Required Script Properties:
 *   SPREADSHEET_ID       The one spreadsheet this deployment may update.
 *   REFLECTION_API_TOKEN A long random token shared with the extension.
 * Optional Script Property:
 *   TEMPLATE_SHEET_NAME  Source for new daily tabs (defaults to Template).
 *
 * Deploy as a web app that executes as you. The extension posts text/plain so
 * the request works cleanly from a Manifest V3 service worker.
 */

const APP_VERSION = '2.3.0';
const ROWS_PER_BLOCK = 16;
const MAX_COLUMN_PAIRS = 100;
const MAX_REFLECTION_LENGTH = 5000;
const NOTE_PREFIX = 'Reflection Timer: ';
const HOUR_THEMES = [
  ['#312e81', '#818cf8'], ['#3730a3', '#a5b4fc'], ['#4c1d95', '#a78bfa'],
  ['#581c87', '#c084fc'], ['#701a75', '#e879f9'], ['#831843', '#f472b6'],
  ['#9a3412', '#fb923c'], ['#c2410c', '#fdba74'], ['#92400e', '#fbbf24'],
  ['#a16207', '#fde047'], ['#facc15', '#a16207'], ['#d9f99d', '#65a30d'],
  ['#166534', '#4ade80'], ['#065f46', '#34d399'], ['#115e59', '#2dd4bf'],
  ['#155e75', '#22d3ee'], ['#075985', '#38bdf8'], ['#1e40af', '#60a5fa'],
  ['#980000', '#ff4d4d'], ['#9f1239', '#fb7185'], ['#9d174d', '#f9a8d4'],
  ['#86198f', '#f0abfc'], ['#6b21a8', '#d8b4fe'], ['#4338ca', '#c7d2fe']
];

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

    const spreadsheet = SpreadsheetApp.openById(config.spreadsheetId);
    if (payload.action === 'ping') {
      const target = resolveTargetSheet_(spreadsheet, payload);
      return jsonOutput_({
        success: true,
        version: APP_VERSION,
        target: `${spreadsheet.getName()} / ${target.sheet ? target.sheet.getName() : target.name}`,
        willCreate: !target.sheet,
        template: target.templateName || null
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
    const target = resolveTargetSheet_(spreadsheet, payload);
    const sheet = target.sheet || createDailySheet_(spreadsheet, target);
    const result = appendReflection_(sheet, message, submissionDate_(spreadsheet, payload), spreadsheet);
    SpreadsheetApp.flush();

    return jsonOutput_({
      success: true,
      version: APP_VERSION,
      sheet: sheet.getName(),
      created: !target.sheet,
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

function resolveTargetSheet_(spreadsheet, payload) {
  // A test may never fall through to the live daily tab, even in fixed mode.
  if (payload.isTest === true) {
    const sheet = spreadsheet.getSheetByName('test');
    if (!sheet) throw new Error('The test tab "test" is missing. Create it before testing; no template or daily tab was changed.');
    return { sheet };
  }
  // Requests from older extension versions have no mode: date routing is the default.
  const mode = payload.sheetMode || 'date';
  if (mode === 'fixed') {
    const name = String(payload.sheetName || '').trim();
    if (!name || name.length > 100) {
      throw new Error('The target tab name is invalid.');
    }
    const sheet = spreadsheet.getSheetByName(name);
    if (!sheet) {
      throw new Error(`The sheet tab "${name}" does not exist.`);
    }
    return { sheet };
  }
  if (mode !== 'date') {
    throw new Error('The target tab mode is invalid.');
  }

  const date = submissionDate_(spreadsheet, payload);
  const matches = spreadsheet.getSheets().map((sheet) => ({
    sheet,
    parts: sheet.getName().trim().match(/^(\d{1,2})\/(\d{1,2})\/(\d{4}|\d{2})$/)
  })).filter(({ parts }) => parts
    && Number(parts[1]) === date.month
    && Number(parts[2]) === date.day
    && Number(parts[3]) === (parts[3].length === 4 ? date.year : date.year % 100));

  // Prefer an explicit four-digit year, then the fully padded name.
  matches.sort((left, right) => right.parts[3].length - left.parts[3].length
    || Number(right.sheet.getName() === date.name) - Number(left.sheet.getName() === date.name)
    || left.sheet.getName().localeCompare(right.sheet.getName()));
  if (matches.length > 0) {
    return { sheet: matches[0].sheet };
  }
  const templateName = String(PropertiesService.getScriptProperties()
    .getProperty('TEMPLATE_SHEET_NAME') || 'Template').trim();
  const template = spreadsheet.getSheetByName(templateName);
  if (!template) {
    throw new Error(`No dated tab matches ${date.name}, and the template tab "${templateName}" is missing. Restore it or set TEMPLATE_SHEET_NAME in Apps Script properties. Nothing was saved.`);
  }
  return { sheet: null, name: date.name, template, templateName };
}

function createDailySheet_(spreadsheet, target) {
  // Called only for a validated reflection while holding the script lock, never by ping.
  const sheet = spreadsheet.insertSheet(target.name, { template: target.template });
  try {
    // Clear copied log contents only: formatting, validation, and the rest of the template stay.
    const columns = Math.min(MAX_COLUMN_PAIRS * 2, Math.floor(sheet.getMaxColumns() / 2) * 2);
    if (columns > 0) {
      sheet.getRange(1, 1, Math.min(ROWS_PER_BLOCK, sheet.getMaxRows()), columns).clearContent();
    }
    // Test entries can now grow beyond 16 rows. Remove every copied A:B log entry and note.
    sheet.getRange(1, 1, sheet.getMaxRows(), Math.min(2, sheet.getMaxColumns()))
      .clearContent().clearNote();
    if (sheet.getMaxRows() < ROWS_PER_BLOCK) {
      sheet.insertRowsAfter(sheet.getMaxRows(), ROWS_PER_BLOCK - sheet.getMaxRows());
    }
    sheet.showSheet();
    return sheet;
  } catch (error) {
    // Roll back only this newly created copy; never remove an existing daily tab or template.
    spreadsheet.deleteSheet(sheet);
    throw error;
  }
}

function submissionDate_(spreadsheet, payload) {
  const timestamp = payload.submittedAt === undefined ? new Date() : new Date(payload.submittedAt);
  if ((payload.submittedAt !== undefined && typeof payload.submittedAt !== 'string')
      || !Number.isFinite(timestamp.getTime())) {
    throw new Error('The submission date is invalid.');
  }

  let parts;
  if (payload.timezoneOffsetMinutes !== undefined) {
    const offset = payload.timezoneOffsetMinutes;
    if (!Number.isInteger(offset) || Math.abs(offset) > 14 * 60) {
      throw new Error('The submission time zone is invalid.');
    }
    // getTimezoneOffset is UTC minus local time; UTC getters avoid the script's zone.
    const localTime = new Date(timestamp.getTime() - offset * 60000);
    parts = [localTime.getUTCFullYear(), localTime.getUTCMonth() + 1, localTime.getUTCDate(),
      localTime.getUTCHours(), localTime.getUTCMinutes()];
  } else {
    parts = Utilities.formatDate(timestamp, spreadsheet.getSpreadsheetTimeZone(), 'yyyy-MM-dd-HH-mm')
      .split('-').map(Number);
  }
  const [year, month, day, hour, minute] = parts;
  const hourStart = timestamp.getTime() - (minute * 60 + timestamp.getUTCSeconds()) * 1000 - timestamp.getUTCMilliseconds();
  return { year, month, day, hour, minute, hourStart, timestamp,
    name: `${String(month).padStart(2, '0')}/${String(day).padStart(2, '0')}/${year}` };
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

function appendReflection_(sheet, message, moment, spreadsheet) {
  ensureColumns_(sheet, 2);
  const previous = previousEntry_(sheet, spreadsheet);
  const needsHour = !previous || previous.hourStart !== moment.hourStart;
  const rows = needsHour ? 2 : 1;
  const background = previous && textColor_(previous.background) === '#000000' ? '#595959' : '#ffffff';
  reserveTopRows_(sheet, rows);
  excludeNewCellsFromConditionalRules_(sheet, rows);

  const entry = sheet.getRange(1, 1, 1, 2);
  // Treat reflections as plain text, including messages beginning with '='.
  entry.setNumberFormat('@');
  const clockLabel = `${moment.hour % 12 || 12}:${String(moment.minute).padStart(2, '0')}`;
  entry.setValues([[clockLabel, message.startsWith('=') ? "'" + message : message]])
    .setFontWeight('normal').setVerticalAlignment('top')
    .setBorder(true, true, true, true, true, false, '#ffffff', SpreadsheetApp.BorderStyle.SOLID)
    .clearNote();
  entry.getCell(1, 1).setBackground(background)
    .setFontColor(textColor_(background)).setNote(NOTE_PREFIX + JSON.stringify({
      kind: 'entry', timestamp: moment.timestamp.toISOString(), hourStart: moment.hourStart
    }));
  entry.getCell(1, 2).setBackground('#0d0d0d').setFontColor('#ffffff').setWrap(true);

  if (needsHour) {
    const theme = HOUR_THEMES[moment.hour];
    const marker = sheet.getRange(2, 1, 1, sheet.getMaxColumns());
    sheet.getRange(2, 1, 1, 2).setNumberFormat('@')
      .setValues([[`${moment.hour % 12 || 12}:00 ${moment.hour < 12 ? 'AM' : 'PM'}`, '']]).clearNote();
    marker.setBackground(theme[0]).setFontColor(textColor_(theme[0]))
      .setFontWeight('bold').setWrap(false)
      .setBorder(true, false, true, false, false, false, theme[1], SpreadsheetApp.BorderStyle.SOLID_MEDIUM);
    marker.getCell(1, 1).setNote(NOTE_PREFIX + JSON.stringify({ kind: 'hour', hourStart: moment.hourStart }));
  }
  return { range: entry.getA1Notation(), timestamp: moment.timestamp };
}

function previousEntry_(sheet, spreadsheet) {
  const count = Math.max(1, sheet.getLastRow());
  const range = sheet.getRange(1, 1, count, 2);
  const values = range.getValues();
  const notes = range.getNotes();
  for (let index = 0; index < count; index += 1) {
    let metadata = {};
    const note = notes[index][0];
    if (note.startsWith(NOTE_PREFIX)) {
      try { metadata = JSON.parse(note.slice(NOTE_PREFIX.length)); } catch (_error) { /* Ordinary note. */ }
    }
    if (metadata.kind === 'hour' || values[index][0] === '' || values[index][1] === '') continue;
    let hourStart = metadata.kind === 'entry' && Number.isFinite(metadata.hourStart) ? metadata.hourStart : null;
    const value = values[index][0];
    if (hourStart === null && value instanceof Date && value.getUTCFullYear() >= 2000) {
      hourStart = submissionDate_(spreadsheet, { submittedAt: value.toISOString() }).hourStart;
    }
    return { hourStart, background: sheet.getRange(index + 1, 1, 1, 1).getBackground() };
  }
  return null;
}

function reserveTopRows_(sheet, rows) {
  if (sheet.getMaxRows() < rows) sheet.insertRowsAfter(sheet.getMaxRows(), rows - sheet.getMaxRows());
  let emptyRows = 0;
  // Reuse only entirely empty rows; keep data in other columns with its original row.
  while (emptyRows < rows && sheet.getRange(emptyRows + 1, 1, 1, sheet.getMaxColumns()).isBlank()) emptyRows += 1;
  const insert = rows - emptyRows;
  if (insert > 0) sheet.insertRowsBefore(1, insert);
}

function excludeNewCellsFromConditionalRules_(sheet, rows) {
  // Protect A1:B1 and the full-width hour row without changing neighboring rules.
  const exclusions = [{ row: 1, col: 1, endRow: 1, endCol: 2 }];
  if (rows === 2) exclusions.push({ row: 2, col: 1, endRow: 2, endCol: sheet.getMaxColumns() });
  const rules = sheet.getConditionalFormatRules();
  let changed = false;
  const updated = [];
  rules.forEach((rule) => {
    const ranges = [];
    rule.getRanges().forEach((range) => {
      const row = range.getRow(), col = range.getColumn();
      const endRow = row + range.getNumRows() - 1, endCol = col + range.getNumColumns() - 1;
      let parts = [{ row, col, endRow, endCol }];
      exclusions.forEach((cut) => {
        parts = parts.flatMap((part) => {
          const top = Math.max(part.row, cut.row), left = Math.max(part.col, cut.col);
          const bottom = Math.min(part.endRow, cut.endRow), right = Math.min(part.endCol, cut.endCol);
          if (top > bottom || left > right) return [part];
          changed = true;
          const remaining = [];
          if (part.row < top) remaining.push({ ...part, endRow: top - 1 });
          if (part.endRow > bottom) remaining.push({ ...part, row: bottom + 1 });
          if (part.col < left) remaining.push({ row: top, endRow: bottom, col: part.col, endCol: left - 1 });
          if (part.endCol > right) remaining.push({ row: top, endRow: bottom, col: right + 1, endCol: part.endCol });
          return remaining;
        });
      });
      parts.forEach((part) => ranges.push(sheet.getRange(part.row, part.col,
        part.endRow - part.row + 1, part.endCol - part.col + 1)));
    });
    if (ranges.length) updated.push(rule.copy().setRanges(ranges).build());
  });
  if (changed) sheet.setConditionalFormatRules(updated);
}

function textColor_(background) {
  const channels = background.replace('#', '').match(/.{2}/g).map((hex) => {
    const channel = parseInt(hex, 16) / 255;
    return channel <= 0.04045 ? channel / 12.92 : Math.pow((channel + 0.055) / 1.055, 2.4);
  });
  const luminance = channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
  return (luminance + 0.05) / 0.05 >= 1.05 / (luminance + 0.05) ? '#000000' : '#ffffff';
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
