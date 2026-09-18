"use strict";

const fs = require('fs');
const path = require('path');

const output = path.resolve(__dirname, '../output');
// dispatcher-tests resolves both its default executable and an explicit x86
// filename against cwd. Mocha loads this file after resolving the test files.
if (process.env.DISPATCHER_MOCK_EXE_PATH)
  process.env.DISPATCHER_MOCK_EXE_PATH = path.resolve(process.env.DISPATCHER_MOCK_EXE_PATH);
process.chdir(output);

const temporary = path.join(output, 'tests', 'legacy');
fs.mkdirSync(temporary, {recursive: true});
process.env.TEMP = temporary;
process.env.TMP = temporary;
