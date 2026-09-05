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
  const formats = new Map();
  const notesBySheet = new Map();
  const insertedCells = [];
  const createdSheets = [];
  const deletedSheets = [];
  function createSheet(name, grid = Array.from({ length: 16 }, () => ['', ''])) {
    grids.set(name, grid);
    const formatsGrid = [];
    const notes = [];
    formats.set(name, formatsGrid);
    notesBySheet.set(name, notes);
    let maxRows = 774;
    let conditionalRules = [];
    const sheet = {
      getName: () => name,
      getMaxColumns: () => 33,
      getMaxRows: () => maxRows,
      getLastRow: () => grid.length,
      insertRowsAfter(_row, count) { maxRows += count; },
      getConditionalFormatRules: () => conditionalRules,
      setConditionalFormatRules(rules) { conditionalRules = rules; },
      showSheet() {},
      insertColumnsAfter() {},
      getRange(row, column, rowCount, columnCount) {
        const read = (data, fallback) => Array.from({ length: rowCount }, (_unused, r) =>
          Array.from({ length: columnCount }, (_item, c) => data[row + r - 1]?.[column + c - 1] ?? fallback));
        const write = (data, values) => {
          for (let r = 0; r < values.length; r += 1) {
            data[row + r - 1] ||= [];
            for (let c = 0; c < values[r].length; c += 1) data[row + r - 1][column + c - 1] = values[r][c];
          }
        };
        const style = (field, value) => {
          const cells = read(formatsGrid, null).map((line) => line.map((item) => ({ ...item, [field]: value })));
          write(formatsGrid, cells);
        };
        return {
          getRow: () => row, getColumn: () => column,
          getNumRows: () => rowCount, getNumColumns: () => columnCount,
          getValues: () => read(grid, ''),
          getNotes: () => read(notes, ''),
          isBlank: () => read(grid, '').every((line) => line.every((value) => value === '')),
          getBackground: () => formatsGrid[row - 1]?.[column - 1]?.background || '#ffffff',
          clearNote() { write(notes, read(notes, '').map((line) => line.map(() => ''))); return this; },
          setNote(value) { write(notes, read(notes, '').map((line) => line.map(() => value))); return this; },
          setBackground(value) { style('background', value); return this; },
          setFontColor(value) { style('fontColor', value); return this; },
          setFontWeight(value) { style('fontWeight', value); return this; },
          setVerticalAlignment(value) { style('verticalAlignment', value); return this; },
          setNumberFormat(value) { style('numberFormat', value); return this; },
          setWrap(value) { style('wrap', value); return this; },
          setBorder(top, left, bottom, right, vertical, horizontal, color, borderStyle) {
            style('borders', { top, left, bottom, right, vertical, horizontal, color, borderStyle }); return this;
          },
          insertCells(dimension) {
            assert.equal(dimension, 'ROWS');
            insertedCells.push({ name, row, column, rowCount, columnCount });
            for (const [data, empty] of [[grid, ''], [notes, ''], [formatsGrid, null]]) {
              for (let r = Math.min(maxRows - 1, data.length + rowCount - 1); r >= row - 1; r -= 1) {
                data[r] ||= [];
                for (let c = column - 1; c < column + columnCount - 1; c += 1) {
                  data[r][c] = r >= row - 1 + rowCount ? data[r - rowCount]?.[c] ?? empty : empty;
                }
              }
            }
            return this;
          },
          getDisplayValues() {
            return Array.from({ length: rowCount }, (_unused, rowOffset) =>
              Array.from({ length: columnCount }, (_item, columnOffset) =>
                grid[row + rowOffset - 1]?.[column + columnOffset - 1] || ''));
          },
          clearContent() {
            if (options.failCopyClear) throw new Error('Copy cleanup failed');
            this.setValues(Array.from({ length: rowCount }, () => Array(columnCount).fill('')));
            return this;
          },
          setValues(values) {
            if (/^\d{1,2}:00 (AM|PM)$/.test(values[0]?.[0])) {
              assert.equal(formatsGrid[row - 1]?.[column - 1]?.numberFormat, '@',
                'format hour labels as text before writing to prevent Sheets time coercion');
            }
            for (let rowOffset = 0; rowOffset < values.length; rowOffset += 1) {
              grid[row + rowOffset - 1] ||= [];
              for (let columnOffset = 0; columnOffset < values[rowOffset].length; columnOffset += 1) {
                grid[row + rowOffset - 1][column + columnOffset - 1] = values[rowOffset][columnOffset];
              }
            }
            return this;
          },
          getCell(r, c) { return sheet.getRange(row + r - 1, column + c - 1, 1, 1); },
          getA1Notation() {
            const endColumn = column + columnCount - 1;
            const endRow = row + rowCount - 1;
            return `${columnLetter(column)}${row}:${columnLetter(endColumn)}${endRow}`;
          }
        };
      }
    };
    return sheet;
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
      Dimension: { ROWS: 'ROWS' },
      BorderStyle: { SOLID: 'SOLID', SOLID_MEDIUM: 'SOLID_MEDIUM' },
      openById(id) {
        assert.equal(id, SPREADSHEET_ID);
        return spreadsheet;
      },
      flush() {}
    },
    Utilities: {
      formatDate(date, timeZone, format) {
        assert.equal(format, 'yyyy-MM-dd-HH-mm');
        const parts = Object.fromEntries(new Intl.DateTimeFormat('en-US', {
          timeZone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23'
        }).formatToParts(date).map((part) => [part.type, part.value]));
        return `${parts.year}-${parts.month}-${parts.day}-${parts.hour}-${parts.minute}`;
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
  return { grid: grids.get('Template'), grids, formats, notesBySheet, insertedCells, request, scriptLock, createdSheets, deletedSheets, context, sheets };
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

test('Apps Script prepends timestamp/activity pairs newest first', () => {
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
  assert.equal(second.range, 'A1:B1');
  assert.equal(harness.grid[0][1], 'Second session');
  assert.equal(harness.grid[1][1], 'First session');
});

test('Apps Script grows A:B past 16 entries without using other column pairs', () => {
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
  assert.equal(result.range, 'A1:B1');
  assert.equal(harness.grid[0][1], 'Session 17');
  assert.equal(harness.grid[16][1], 'Session 1');
  assert.equal(harness.grid[0][3], undefined);
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

test('hour markers follow the example and entry colors alternate across the divider', () => {
  const harness = createHarness(['Temp']);
  for (const time of ['17:54', '17:55', '17:57', '17:58', '18:21']) {
    const result = harness.request({ ...datedRequest, isTest: true,
      submittedAt: `2026-09-05T${time}:00-04:00`, message: `Test ${time}` });
    assert.equal(result.success, true, result.error);
  }
  const grid = harness.grids.get('Temp');
  const format = harness.formats.get('Temp');
  assert.equal(grid[0][1], 'Test 18:21');
  assert.equal(grid[0][0], '6:21');
  assert.equal(grid[1][0], '6:00 PM');
  assert.equal(grid[2][1], 'Test 17:58');
  assert.equal(grid[3][1], 'Test 17:57');
  assert.equal(grid[4][1], 'Test 17:55');
  assert.equal(format[0][0].background, '#ffffff');
  assert.equal(format[0][0].fontColor, '#000000');
  assert.equal(format[2][0].background, '#595959');
  assert.equal(format[2][0].fontColor, '#ffffff');
  assert.equal(format[3][0].background, '#ffffff');
  assert.equal(format[4][0].background, '#595959');
  assert.equal(format[1][0].background, '#980000');
  assert.equal(format[1][1].background, '#980000');
  assert.deepEqual(format[1][0].borders, { top: true, bottom: true,
    left: false, right: false, vertical: false, horizontal: false,
    color: '#ff4d4d', borderStyle: 'SOLID_MEDIUM' });
});

test('reused and inserted entry cells have thin white borders without changing hour dividers', () => {
  const harness = createHarness(['Temp']);
  for (const time of ['17:58', '17:59', '18:01', '18:02']) {
    const result = harness.request({ ...datedRequest, isTest: true,
      submittedAt: `2026-09-05T${time}:00-04:00` });
    assert.equal(result.success, true, result.error);
    const format = harness.formats.get('Temp');
    for (const cell of format[0].slice(0, 2)) {
      assert.deepEqual(cell.borders, { top: true, left: true, bottom: true,
        right: true, vertical: true, horizontal: false, color: '#ffffff', borderStyle: 'SOLID' });
    }
    const notes = harness.notesBySheet.get('Temp');
    for (let row = 0; row < notes.length; row += 1) {
      if (!notes[row]?.[0]?.includes('"kind":"hour"')) continue;
      for (const cell of format[row].slice(0, 2)) {
        assert.equal(cell.borders.top, true);
        assert.equal(cell.borders.bottom, true);
        assert.equal(cell.borders.left, false);
        assert.equal(cell.borders.right, false);
        assert.equal(cell.borders.vertical, false);
        assert.notEqual(cell.borders.color, '#ffffff');
        assert.equal(cell.borders.borderStyle, 'SOLID_MEDIUM');
      }
    }
  }
});

test('empty top pairs are reused, while partial pairs and empty-result formulas are preserved', () => {
  const empty = createHarness(['Temp']);
  empty.request({ ...datedRequest, isTest: true });
  assert.equal(empty.insertedCells.length, 0);
  for (const pair of [['existing time', ''], ['', 'existing note'], ['=""', '']]) {
    const harness = createHarness(['Temp']);
    harness.grids.get('Temp')[0] = [...pair, 'C stays here', 'D stays here'];
    assert.equal(harness.request({ ...datedRequest, isTest: true }).success, true);
    assert.deepEqual(harness.grids.get('Temp')[2].slice(0, 2), pair);
    assert.deepEqual(harness.grids.get('Temp')[0].slice(2), ['C stays here', 'D stays here']);
    assert.equal(harness.insertedCells[0].columnCount, 2);
  }
});

test('new hours get different readable themes, including midnight and noon', () => {
  const harness = createHarness(['Temp']);
  const themes = new Set();
  for (let hour = 0; hour < 24; hour += 1) {
    harness.request({ ...datedRequest, isTest: true,
      submittedAt: `2026-09-05T${String(hour).padStart(2, '0')}:21:00-04:00` });
    const grid = harness.grids.get('Temp');
    const format = harness.formats.get('Temp')[1][0];
    assert.equal(grid[1][0], `${hour % 12 || 12}:00 ${hour < 12 ? 'AM' : 'PM'}`);
    assert.equal(format.fontColor, harness.context.textColor_(format.background));
    assert.notEqual(format.background, format.borders.color);
    themes.add(format.background);
  }
  assert.equal(themes.size, 24);
});

test('same-hour entries do not duplicate the divider and time gaps add only the current hour', () => {
  const harness = createHarness(['Temp']);
  for (const time of ['12:01', '12:59', '15:04']) {
    harness.request({ ...datedRequest, isTest: true, submittedAt: `2026-09-05T${time}:00-04:00` });
  }
  const markers = harness.grids.get('Temp').filter((row) => /AM|PM/.test(row[0]));
  assert.deepEqual(markers.map((row) => row[0]), ['3:00 PM', '12:00 PM']);
});

test('test writes always use Temp and never create or write a daily tab', () => {
  const harness = createHarness(['Temp', '09/05/2026', 'Custom']);
  const result = harness.request({ ...datedRequest, isTest: true, sheetMode: 'fixed', sheetName: 'Custom' });
  assert.equal(result.sheet, 'Temp');
  assert.deepEqual(harness.grids.get('Custom')[0], ['', '']);
  assert.deepEqual(harness.grids.get('09/05/2026')[0], ['', '']);
  assert.equal(harness.createdSheets.length, 0);
  const missingTemp = createHarness(['09/05/2026']);
  const failure = missingTemp.request({ ...datedRequest, isTest: true });
  assert.equal(failure.success, false);
  assert.match(failure.error, /test tab "Temp" is missing/);
  assert.deepEqual(missingTemp.grids.get('09/05/2026')[0], ['', '']);
});

test('newly written cells are excluded from old conditional rules without changing neighbors', () => {
  const harness = createHarness(['Temp']);
  const sheet = harness.sheets[0];
  const makeRule = (ranges) => ({ getRanges: () => ranges,
    copy() { return { setRanges(next) { return { build: () => makeRule(next) }; } }; } });
  sheet.setConditionalFormatRules([makeRule([sheet.getRange(1, 1, 774, 33)])]);
  harness.request({ ...datedRequest, isTest: true });
  const ranges = sheet.getConditionalFormatRules()[0].getRanges();
  assert.deepEqual(Array.from(ranges, (r) => r.getA1Notation()), ['C1:AG774', 'A3:B774']);
});

test('reflections beginning with equals are stored as literal text', () => {
  const harness = createHarness(['Temp']);
  harness.request({ ...datedRequest, isTest: true, message: '=1+1' });
  assert.equal(harness.grids.get('Temp')[0][1], "'=1+1");
  assert.equal(harness.formats.get('Temp')[0][1].numberFormat, '@');
});

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
  harness.grid[16] = ['old log', 'old test', '=SUM(C1:C16)', 'keep outside log area'];
  harness.grid[0][32] = 'keep unpaired column';
  const original = structuredClone(harness.grid);
  const first = harness.request(datedRequest);
  assert.equal(first.success, true);
  assert.equal(first.sheet, '09/05/2026');
  assert.equal(first.created, true);
  assert.equal(first.range, 'A1:B1');
  const dailyGrid = harness.grids.get(first.sheet);
  assert.equal(dailyGrid[0][1], datedRequest.message);
  assert.deepEqual(dailyGrid[16], ['', '', ...original[16].slice(2)]);
  assert.equal(dailyGrid[0][32], 'keep unpaired column');
  assert.deepEqual(harness.grid, original, 'source template is never cleared');
  const second = harness.request({ ...datedRequest, message: 'Next session' });
  assert.equal(second.created, false);
  assert.equal(second.range, 'A1:B1');
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
