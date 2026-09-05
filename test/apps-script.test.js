'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '..', 'google-sheets-script.gs'), 'utf8');
const SPREADSHEET_ID = 'synthetic-spreadsheet-id-for-tests';
const TOKEN = 'test-token-that-is-long-enough';

function columnLetter(column) {
  let result = '';
  for (let value = column; value > 0; value = Math.floor((value - 1) / 26)) {
    result = String.fromCharCode(65 + ((value - 1) % 26)) + result;
  }
  return result;
}

function createHarness(names = ['Template'], timezone = 'America/New_York', options = {}) {
  const grids = new Map();
  const createdSheets = [];
  const deletedSheets = [];
  function createSheet(name, grid = Array.from({ length: 16 }, () => ['', ''])) {
    grids.set(name, grid);
    return {
      getName: () => name,
      getMaxColumns: () => 33,
      getMaxRows: () => 774,
      showSheet() {},
      insertColumnsAfter() {},
      getRange(row, column, rowCount, columnCount) {
        return {
          getDisplayValues() {
            return Array.from({ length: rowCount }, (_unused, rowOffset) =>
              Array.from({ length: columnCount }, (_item, columnOffset) =>
                grid[row + rowOffset - 1]?.[column + columnOffset - 1] || ''));
          },
          clearContent() {
            if (options.failCopyClear) throw new Error('Copy cleanup failed');
            this.setValues(Array.from({ length: rowCount }, () => Array(columnCount).fill('')));
          },
          setValues(values) {
            for (let rowOffset = 0; rowOffset < values.length; rowOffset += 1) {
              grid[row + rowOffset - 1] ||= [];
              for (let columnOffset = 0; columnOffset < values[rowOffset].length; columnOffset += 1) {
                grid[row + rowOffset - 1][column + columnOffset - 1] = values[rowOffset][columnOffset];
              }
            }
            return this;
          },
          getCell() { return { setNumberFormat() {}, setWrap() {} }; },
          getA1Notation() {
            const endColumn = column + columnCount - 1;
            const endRow = row + rowCount - 1;
            return `${columnLetter(column)}${row}:${columnLetter(endColumn)}${endRow}`;
          }
        };
      }
    };
  }
  const sheets = names.map((name) => createSheet(name));
  const spreadsheet = {
    getName: () => 'Timer Activities',
    getSheetByName: (name) => sheets.find((sheet) => sheet.getName() === name) || null,
    getSheets: () => sheets,
    getSpreadsheetTimeZone: () => timezone,
    insertSheet(name, { template }) {
      assert.equal(scriptLock.locked, true, 'daily tab creation must be locked');
      assert.equal(grids.has(name), false, 'do not replace an existing daily tab');
      const sheet = createSheet(name, structuredClone(grids.get(template.getName())));
      sheets.push(sheet);
      createdSheets.push(sheet);
      return sheet;
    },
    deleteSheet(sheet) {
      assert.ok(createdSheets.includes(sheet), 'only roll back a newly created copy');
      deletedSheets.push(sheet);
      grids.delete(sheet.getName());
      sheets.splice(sheets.indexOf(sheet), 1);
    }
  };
  const scriptLock = {
    locked: false,
    waitLock() { this.locked = true; },
    hasLock() { return this.locked; },
    releaseLock() { this.locked = false; }
  };
  const context = {
    console: { error() {}, log() {} },
    ContentService: {
      MimeType: { JSON: 'application/json' },
      createTextOutput(text) {
        return { text, setMimeType() { return this; } };
      }
    },
    LockService: { getScriptLock: () => scriptLock },
    PropertiesService: {
      getScriptProperties: () => ({
        getProperty(name) {
          if (name === 'TEMPLATE_SHEET_NAME') return options.templateName || '';
          return name === 'SPREADSHEET_ID' ? SPREADSHEET_ID : TOKEN;
        }
      })
    },
    SpreadsheetApp: {
      openById(id) {
        assert.equal(id, SPREADSHEET_ID);
        return spreadsheet;
      },
      flush() {}
    },
    Utilities: {
      formatDate(date, timeZone, format) {
        assert.equal(format, 'yyyy-MM-dd');
        const parts = Object.fromEntries(new Intl.DateTimeFormat('en-US', {
          timeZone, year: 'numeric', month: '2-digit', day: '2-digit'
        }).formatToParts(date).map((part) => [part.type, part.value]));
        return `${parts.year}-${parts.month}-${parts.day}`;
      },
      Charset: { UTF_8: 'utf8' },
      DigestAlgorithm: { SHA_256: 'sha256' },
      computeDigest(_algorithm, value) {
        return [...crypto.createHash('sha256').update(value, 'utf8').digest()];
      }
    }
  };
  vm.runInNewContext(source, context, { filename: 'google-sheets-script.gs' });
  const request = (payload) => JSON.parse(context.doPost({ postData: { contents: JSON.stringify(payload) } }).text);
  return { grid: grids.get('Template'), grids, request, scriptLock, createdSheets, deletedSheets };
}

test('Apps Script ping validates configuration and returns the target', () => {
  const harness = createHarness();
  const result = harness.request({
    action: 'ping',
    token: TOKEN,
    sheetUrl: `https://docs.google.com/spreadsheets/d/${SPREADSHEET_ID}/edit`,
    sheetName: 'Template',
    sheetMode: 'fixed'
  });
  assert.equal(result.success, true);
  assert.equal(result.target, 'Timer Activities / Template');
});

test('Apps Script fills timestamp/activity pairs in order', () => {
  const harness = createHarness();
  const base = {
    action: 'appendReflection',
    token: TOKEN,
    sheetUrl: `https://docs.google.com/spreadsheets/d/${SPREADSHEET_ID}/edit`,
    sheetName: 'Template',
    sheetMode: 'fixed'
  };
  const first = harness.request({ ...base, message: 'First session' });
  const second = harness.request({ ...base, message: 'Second session' });
  assert.equal(first.range, 'A1:B1');
  assert.equal(second.range, 'A2:B2');
  assert.equal(harness.grid[0][1], 'First session');
  assert.equal(harness.grid[1][1], 'Second session');
});

test('Apps Script moves to the next column pair after 16 entries', () => {
  const harness = createHarness();
  const base = {
    action: 'appendReflection',
    token: TOKEN,
    sheetUrl: `https://docs.google.com/spreadsheets/d/${SPREADSHEET_ID}/edit`,
    sheetName: 'Template',
    sheetMode: 'fixed'
  };
  let result;
  for (let index = 1; index <= 17; index += 1) {
    result = harness.request({ ...base, message: `Session ${index}` });
  }
  assert.equal(result.range, 'C1:D1');
  assert.equal(harness.grid[0][3], 'Session 17');
});

test('Apps Script rejects an invalid token without writing', () => {
  const harness = createHarness();
  const result = harness.request({
    action: 'appendReflection',
    token: 'wrong-token',
    sheetUrl: `https://docs.google.com/spreadsheets/d/${SPREADSHEET_ID}/edit`,
    sheetName: 'Template',
    sheetMode: 'fixed',
    message: 'Should not be written'
  });
  assert.equal(result.success, false);
  assert.equal(result.error, 'Unauthorized request.');
  assert.deepEqual(harness.grid[0], ['', '']);
});

const datedRequest = {
  action: 'appendReflection', token: TOKEN, sheetUrl: SPREADSHEET_ID,
  message: 'A dated reflection', submittedAt: '2026-09-06T02:30:00.000Z',
  timezoneOffsetMinutes: 240
};

test('default routing accepts two/four digit years and optional leading zeros', () => {
  for (const name of ['09/05/2026', '09/05/26', '9/5/26', '9/05/2026', ' 9/5/2026 ']) {
    const harness = createHarness(['Template', name]);
    const result = harness.request({ ...datedRequest, sheetName: 'Template' });
    assert.equal(result.success, true, name);
    assert.equal(result.sheet, name);
    assert.equal(harness.grids.get(name)[0][1], datedRequest.message);
    assert.deepEqual(harness.grid[0], ['', '']);
    assert.equal(harness.scriptLock.locked, false);
  }
});

test('explicit date mode ignores a stale fixed target', () => {
  const harness = createHarness(['Template', '09/05/2026']);
  const result = harness.request({ ...datedRequest, sheetMode: 'date', sheetName: 'Template' });
  assert.equal(result.sheet, '09/05/2026');
});

test('the send date uses the browser local zone, not the UTC date or completion date', () => {
  const harness = createHarness(['09/05/2026', '09/06/2026', '09/07/2026']);
  const beforeMidnight = harness.request({ ...datedRequest, completedAt: '2026-09-05T00:00:00Z' });
  assert.equal(beforeMidnight.sheet, '09/05/2026');
  const afterMidnight = harness.request({ ...datedRequest, submittedAt: '2026-09-06T04:01:00Z' });
  assert.equal(afterMidnight.sheet, '09/06/2026');
  const eastOfUtc = harness.request({ ...datedRequest, submittedAt: '2026-09-06T19:00:00Z', timezoneOffsetMinutes: -330 });
  assert.equal(eastOfUtc.sheet, '09/07/2026');
});

test('date routing crosses New Year correctly and matches two-digit years', () => {
  const harness = createHarness(['12/31/26', '01/01/2027']);
  const result = harness.request({ ...datedRequest, submittedAt: '2027-01-01T02:30:00Z' });
  assert.equal(result.sheet, '12/31/26');
});

test('four-digit year then padded naming takes priority, regardless of tab order', () => {
  const harness = createHarness(['09/05/26', '9/5/2026', '09/05/2026']);
  const result = harness.request(datedRequest);
  assert.equal(result.sheet, '09/05/2026');
});

test('missing date and template tabs never fall back to another year or a yearless tab', () => {
  const harness = createHarness(['09/05/2025', '09/05', '05/09/2026']);
  const result = harness.request(datedRequest);
  assert.equal(result.success, false);
  assert.match(result.error, /No dated tab matches 09\/05\/2026/);
  assert.match(result.error, /template tab "Template" is missing/);
  assert.match(result.error, /Nothing was saved/);
  for (const grid of harness.grids.values()) assert.deepEqual(grid[0], ['', '']);
  assert.equal(harness.scriptLock.locked, false);
});

test('the first send copies Template once and clears only the copy log area', () => {
  const harness = createHarness();
  harness.grid[0] = ['old timestamp', 'old reflection'];
  harness.grid[16] = ['=SUM(A1:A16)', 'keep outside log area'];
  harness.grid[0][32] = 'keep unpaired column';
  const original = structuredClone(harness.grid);
  const first = harness.request(datedRequest);
  assert.equal(first.success, true);
  assert.equal(first.sheet, '09/05/2026');
  assert.equal(first.created, true);
  assert.equal(first.range, 'A1:B1');
  const dailyGrid = harness.grids.get(first.sheet);
  assert.equal(dailyGrid[0][1], datedRequest.message);
  assert.deepEqual(dailyGrid[16], original[16]);
  assert.equal(dailyGrid[0][32], 'keep unpaired column');
  assert.deepEqual(harness.grid, original, 'source template is never cleared');
  const second = harness.request({ ...datedRequest, message: 'Next session' });
  assert.equal(second.created, false);
  assert.equal(second.range, 'A2:B2');
  assert.equal(harness.createdSheets.length, 1);
});

test('ping previews creation without creating a tab or clearing the template', () => {
  const harness = createHarness(['Temp'], 'America/New_York', { templateName: 'Temp' });
  const result = harness.request({ ...datedRequest, action: 'ping' });
  assert.equal(result.success, true);
  assert.equal(result.target, 'Timer Activities / 09/05/2026');
  assert.equal(result.willCreate, true);
  assert.equal(result.template, 'Temp');
  assert.equal(harness.createdSheets.length, 0);
  assert.equal(harness.grids.size, 1);
});

test('new tabs support a configured template name, but existing dated tabs need no template', () => {
  const harness = createHarness(['Temp'], 'America/New_York', { templateName: 'Temp' });
  assert.equal(harness.request(datedRequest).sheet, '09/05/2026');
  const existingOnly = createHarness(['9/5/26']);
  assert.equal(existingOnly.request(datedRequest).sheet, '9/5/26');
});

test('bad messages cannot create a daily tab', () => {
  for (const message of ['', 'x'.repeat(5001)]) {
    const harness = createHarness();
    assert.equal(harness.request({ ...datedRequest, message }).success, false);
    assert.equal(harness.createdSheets.length, 0);
  }
});

test('a failed new-tab initialization rolls back only the copy', () => {
  const harness = createHarness(['Template'], 'America/New_York', { failCopyClear: true });
  harness.grid[0] = ['old timestamp', 'old reflection'];
  const result = harness.request(datedRequest);
  assert.equal(result.success, false);
  assert.equal(result.error, 'Copy cleanup failed');
  assert.equal(harness.deletedSheets.length, 1);
  assert.deepEqual(harness.grid[0], ['old timestamp', 'old reflection']);
  assert.equal(harness.grids.has('09/05/2026'), false);
  assert.equal(harness.scriptLock.locked, false);
});

test('dated connection tests select the target without writing', () => {
  const harness = createHarness(['09/05/26']);
  const result = harness.request({ ...datedRequest, action: 'ping' });
  assert.equal(result.target, 'Timer Activities / 09/05/26');
  assert.deepEqual(harness.grids.get('09/05/26')[0], ['', '']);
});

test('clients without a time-zone offset use the spreadsheet time zone', () => {
  const harness = createHarness(['09/05/26'], 'America/Los_Angeles');
  const result = harness.request({ ...datedRequest, timezoneOffsetMinutes: undefined });
  assert.equal(result.sheet, '09/05/26');
});

test('invalid date, time zone, mode, token, and spreadsheet are rejected without writes', () => {
  for (const invalid of [
    { submittedAt: 'not-a-date' }, { submittedAt: null },
    { timezoneOffsetMinutes: '240' }, { timezoneOffsetMinutes: 841 },
    { timezoneOffsetMinutes: 1.5 }, { sheetMode: 'typo' },
    { token: 'wrong' }, { sheetUrl: 'other-spreadsheet-id-is-not-allowed' }
  ]) {
    const harness = createHarness(['09/05/2026']);
    const result = harness.request({ ...datedRequest, ...invalid });
    assert.equal(result.success, false, JSON.stringify(invalid));
    assert.deepEqual(harness.grids.get('09/05/2026')[0], ['', '']);
  }
});
