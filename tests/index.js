"use strict";

const spawn = require('child_process').spawn;

const drain = require('nyks/stream/drain');
const wait = require('nyks/child_process/wait');

const expect = require('expect.js');
const {mock, getProcessList} = require('./_mock.js');

const sleep = require('nyks/async/sleep');




describe("Initial test suite", function(){
  let cleanups = [];
  this.timeout(60 * 1000);

  after("cleanup", async() => {
    for(let mock of cleanups)
      await mock.cleanup();
  });

  it("Should forward stdout", async () => {
      let tmp = new mock();
      cleanups.push(tmp);

      let child = spawn(tmp.execPath, ['-p', '1+2']);
      let [exit, stdout] = await Promise.all([wait(child), drain(child.stdout)]);
      let body = String(stdout);

      expect(body).to.eql("3\n");
  });


  it("Should forward stderr", async () => {
      let tmp = new mock();
      cleanups.push(tmp);

      let child = spawn(tmp.execPath, ['-e', "console.error(42)"]);
      let [exit, stdout, stderr] = await Promise.all([wait(child), drain(child.stdout), drain(child.stderr)]);
      stdout = String(stdout);
      stderr = String(stderr);

      expect(stdout).to.eql("");
      expect(stderr).to.eql("42\n");
  });




  it("Should escape args", async () => {
      let tmp = new mock();
      cleanups.push(tmp);

      let args = ["42", "", " ", "The sun is shinning", "Ho I' <", 'With a quoted " '];
      let child = spawn(tmp.execPath, ["-p", "JSON.stringify(process.argv.slice(1))", ...args]);
      let [exit, stdout, stderr] = await Promise.all([wait(child), drain(child.stdout), drain(child.stderr)]);
      stdout = String(stdout);
      stderr = String(stderr);

      expect(stdout).to.eql(JSON.stringify(args) + "\n");
      expect(stderr).to.eql("");
  });



  it("Should escape args in configuration", async () => {

      let args = ["42", "", " ", "The sun is & &quote; shinning", "Ho I' <", 'With a quoted " '];
      let args2 = ["1", "2 "];

      let l = ["-p", "JSON.stringify(process.argv.slice(1))", ...args];
      let dict = l.reduce((acc, val, k) => (acc[`ARGV${k}`] = val, acc), {});

      let tmp = new mock(dict);
      let child = spawn(tmp.execPath, args2);
      let [exit, stdout, stderr] = await Promise.all([wait(child), drain(child.stdout), drain(child.stderr)]);
      stdout = String(stdout);
      stderr = String(stderr);

      expect(stdout).to.eql(JSON.stringify([...args, ...args2]) + "\n");
      expect(stderr).to.eql("");
      //cleanups.push(tmp);
  });



  it("Should forward args", async () => {
      let tmp = new mock({ARGV4 : "JSON.stringify(process.argv.slice(1))", ARGV0 : '-p'});
      cleanups.push(tmp);

      let args = ["42", "", " ", "The sun is shinning", "Ho I' <", 'With a quoted " '];
      let child = spawn(tmp.execPath, args);
      let [exit, stdout, stderr] = await Promise.all([wait(child), drain(child.stdout), drain(child.stderr)]);
      stdout = String(stdout);
      stderr = String(stderr);

      expect(stdout).to.eql(JSON.stringify(args) + "\n");
      expect(stderr).to.eql("");
  });




  it("Should configure ENV", async () => {
      let ENV_FOOBAR = "ok boomer";
      let tmp = new mock({ARGV4 : "process.env.FOOBAR", ARGV0 : '-p', ENV_FOOBAR});
      cleanups.push(tmp);

      let child = spawn(tmp.execPath);
      let [exit, stdout, stderr] = await Promise.all([wait(child), drain(child.stdout), drain(child.stderr)]);
      stdout = String(stdout);
      stderr = String(stderr);

      expect(stdout).to.eql(`${ENV_FOOBAR}\n`);
      expect(stderr).to.eql("");
  });


});


describe("Jobs tests suite", function() {
  let cleanups = [];
  this.timeout(60 * 1000);

  after("cleanup", async() => {
    for(let mock of cleanups)
      await mock.cleanup();
  });

  it("should kill main process on subchild exit", async () => {
      let tmp = new mock();
      cleanups.push(tmp);

      let main = spawn(tmp.execPath, ['-e', "setInterval(a=>0, 1000)"]);

      let foo = await getProcessList();
      let dispatched = foo.find(line => line.ParentProcessId == main.pid)

      process.kill(dispatched.ProcessId);
      let end = await new Promise(resolve => main.on('exit', resolve));
  });


  it("should kill child on dispatched exit (with job)", async () => {
      let tmp = new mock();
      cleanups.push(tmp);

      let main = spawn(tmp.execPath,  ['-e', "setInterval(a=>0, 1000)"]);

      let before = await getProcessList();
      let dispatched = before.find(line => line.ParentProcessId == main.pid)
      main.kill();

      let after = await getProcessList();
      let dispatchedAfter = after.find(line => line.ProcessId == dispatched.processId)
      expect(dispatchedAfter).to.eql(undefined);
  });



  it("should preserve child on dispatched exit (without job)", async () => {
      let tmp = new mock({USE_JOB : "false"});
      //cleanups.push(tmp);

      let main = spawn(tmp.execPath,  ['-e', "setInterval(a=>0, 1000)"]);

      let before = await getProcessList();
      let dispatched = before.find(line => line.ParentProcessId == main.pid)
      main.kill();

      let after = await getProcessList();
      let dispatchedAfter = after.find(line => line.ProcessId == dispatched.ProcessId)
      expect(dispatchedAfter).to.eql(dispatched);
      process.kill(dispatchedAfter.ProcessId);
  });





});


