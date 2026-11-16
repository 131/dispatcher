'use strict';

const fs = require('fs');
const path = require('path');
const {execFileSync} = require('child_process');

const version = process.env.npm_package_version;
if(!/^\d+\.\d+\.\d+$/.test(version || ''))
  throw new Error('ppackage must supply npm_package_version');
const major = Number(version.split('.')[0]);
const modulePath = 'github.com/131/dispatcher' + (major >= 2 ? `/v${major}` : '');
const files = ['src/Properties/AssemblyInfo.cs'];
const assembly = fs.readFileSync(files[0], 'utf8').replace(
  /(AssemblyVersion|AssemblyFileVersion)\("[^"]*"\)/g,
  `$1("${version}.0")`
);
fs.writeFileSync(files[0], assembly);

// ppackage owns go.mod/version markers; the hook aligns this module's imports.
function rewriteImports(dir) {
  for(const entry of fs.readdirSync(dir, {withFileTypes : true})) {
    const file = path.join(dir, entry.name);
    if(entry.isDirectory())
      rewriteImports(file);
    else if(file.endsWith('.go')) {
      const original = fs.readFileSync(file, 'utf8');
      const rewritten = original.replace(/github\.com\/131\/dispatcher(?:\/v\d+)?(?=\/|"|$)/g, modulePath);
      if(original !== rewritten) {
        fs.writeFileSync(file, rewritten);
        files.push(file);
      }
    }
  }
}
rewriteImports('cmd');
rewriteImports('internal');
execFileSync('git', ['add', ...files], {stdio : 'inherit'});
