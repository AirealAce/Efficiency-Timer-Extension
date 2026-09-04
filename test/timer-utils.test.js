'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const {
  durationFromParts,
  extractSpreadsheetId,
  getRemainingSeconds,
  isSupportedPageUrl,
  isValidWebAppUrl,
  splitDuration
} = require('../timer-utils.js');

test('durationFromParts normalizes the three timer fields', () => {
  assert.equal(durationFromParts({ hours: '1', minutes: '2', seconds: '3' }), 3723);
  assert.equal(durationFromParts({ hours: '-1', minutes: '99', seconds: '90' }), 3599);
});

test('splitDuration produces display-ready parts', () => {
  assert.deepEqual(splitDuration(3723), { hours: 1, minutes: 2, seconds: 3 });
});

test('getRemainingSeconds derives running time from a deadline', () => {
  assert.equal(getRemainingSeconds({ isRunning: true, endTime: 12_001 }, 10_000), 3);
  assert.equal(getRemainingSeconds({ isRunning: true, endTime: 9_000 }, 10_000), 0);
  assert.equal(getRemainingSeconds({ isRunning: false, remainingSeconds: 42 }, 10_000), 42);
});

test('extractSpreadsheetId accepts a Sheets URL or a bare ID', () => {
  const id = 'synthetic-spreadsheet-id-for-tests';
  assert.equal(extractSpreadsheetId(`https://docs.google.com/spreadsheets/d/${id}/edit`), id);
  assert.equal(extractSpreadsheetId(id), id);
  assert.equal(extractSpreadsheetId('https://example.com/not-a-sheet'), null);
});

test('web app validation only accepts deployed Apps Script URLs', () => {
  assert.equal(isValidWebAppUrl('https://script.google.com/macros/s/abc123/exec'), true);
  assert.equal(isValidWebAppUrl('https://script.google.com/macros/s/abc123/dev'), false);
  assert.equal(isValidWebAppUrl('https://evil.example/macros/s/abc123/exec'), false);
});

test('page validation excludes privileged browser pages', () => {
  assert.equal(isSupportedPageUrl('https://example.com'), true);
  assert.equal(isSupportedPageUrl('file:///C:/notes.html'), true);
  assert.equal(isSupportedPageUrl('chrome://extensions'), false);
});
