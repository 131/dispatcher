"use strict";

const fs = require('fs');
const path  =require('path');
const csvParse = require('csv-parse/lib/sync');
const spawn = require('child_process').spawn;

const wait = require('nyks/child_process/wait');
const drain = require('nyks/stream/drain');

const mkdirpSync = require('nyks/fs/mkdirpSync');
const tmppath = require('nyks/fs/tmppath');

const guid = require('mout/random/guid');
const escapeHtml = require('mout/string/escapeHtml');
const rmrf = require('nyks/fs/rmrf');




async function getProcessList() {
  let args = ["wmic", "PROCESS", "get", "ParentProcessId,ProcessId,Caption,ExecutablePath", "/format:csv"];
  let child = spawn(args.shift(), args);
  let [, stdout] = await Promise.all([wait(child), drain(child.stdout)]);
  var data =  csvParse(String(stdout).replace(/\r\r/g, '\n'), {columns : true, skip_empty_lines : true});

  return data;
  
}


class mock {

  constructor(lines = {}, dispatcher = "dispatcher_cmd.exe") {

    const dispatcher_path = path.resolve("..", dispatcher);

    const wd = tmppath();
    mkdirpSync(wd);

    let name = guid();

    let execPath = path.join(wd, `${name}.exe`);
    let configPath = path.join(wd, `${name}.config`);

    fs.copyFileSync(dispatcher_path, execPath);
    if(!lines["PATH"])
      lines["PATH"] = process.execPath;

    let configBody = [
      `<?xml version="1.0" encoding="utf-8" ?>`,
      `<configuration>`,
      `  <appSettings>`,
      ...(Object.keys(lines).map(
          key => `    <add key="${key}" value="${escapeHtml(lines[key])}"/>`
      )),
      `  </appSettings>`,
      `</configuration>` ].join("\n")
    fs.writeFileSync(configPath, configBody);

    this.wd = wd;
    this.name = name;
    this.execPath = execPath;
    this.configPath = configPath;
  }

  
  async cleanup() {
    await rmrf(this.wd);
  }
  


}


module.exports = {mock, getProcessList};
