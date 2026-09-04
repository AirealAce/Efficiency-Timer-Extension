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

function createHarness() {
  const grid = Array.from({ length: 16 }, () => ['', '']);
  const sheet = {
    getName: () => 'Template',
    getMaxColumns: () => 33,
    insertColumnsAfter() {},
    getRange(row, column, rowCount, columnCount) {
      return {
        getDisplayValues() {
          return Array.from({ length: rowCount }, (_unused, rowOffset) =>
            Array.from({ length: columnCount }, (_item, columnOffset) =>
              grid[row + rowOffset - 1]?.[column + columnOffset - 1] || ''));
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
  const spreadsheet = {
    getName: () => 'Timer Activities',
    getSheetByName: (name) => name === 'Template' ? sheet : null
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
      Charset: { UTF_8: 'utf8' },
      DigestAlgorithm: { SHA_256: 'sha256' },
      computeDigest(_algorithm, value) {
        return [...crypto.createHash('sha256').update(value, 'utf8').digest()];
      }
    }
  };
  vm.runInNewContext(source, context, { filename: 'google-sheets-script.gs' });
  const request = (payload) => JSON.parse(context.doPost({ postData: { contents: JSON.stringify(payload) } }).text);
  return { grid, request };
}

test('Apps Script ping validates configuration and returns the target', () => {
  const harness = createHarness();
  const result = harness.request({
    action: 'ping',
    token: TOKEN,
    sheetUrl: `https://docs.google.com/spreadsheets/d/${SPREADSHEET_ID}/edit`,
    sheetName: 'Template'
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
    sheetName: 'Template'
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
    sheetName: 'Template'
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
    message: 'Should not be written'
  });
  assert.equal(result.success, false);
  assert.equal(result.error, 'Unauthorized request.');
  assert.deepEqual(harness.grid[0], ['', '']);
});
