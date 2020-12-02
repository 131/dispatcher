"use strict";

const fs = require('fs');
const path = require('path');
const spawn = require('child_process').spawn;

const drain = require('nyks/stream/drain');
const wait = require('nyks/child_process/wait');
const defer = require('nyks/promise/defer');

const expect = require('expect.js');
const {mock, getProcessList} = require('./_mock.js');

const sleep = require('nyks/async/sleep');

const promisify = require('nyks/function/promisify');
const passthru = promisify(require('nyks/child_process/passthru'));

const http = require('http');



describe("Initial test suite", function(){
  let cleanups = [];
  this.timeout(60 * 1000);

  after("cleanup", async() => {
    for(let mock of cleanups)
      await mock.cleanup();
  });

  it("Should forward stdout", async () => {
      let tmp = new mock();

      let child = spawn(tmp.execPath, ['-p', '1+2']);
      let [exit, stdout] = await Promise.all([wait(child), drain(child.stdout)]);
      let body = String(stdout);

      expect(body).to.eql("3\n");


      cleanups.push(tmp);
  });


  it("Should forward stderr", async () => {
      let tmp = new mock();

      let child = spawn(tmp.execPath, ['-e', "console.error(42)"]);
      let [exit, stdout, stderr] = await Promise.all([wait(child), drain(child.stdout), drain(child.stderr)]);
      stdout = String(stdout);
      stderr = String(stderr);

      expect(stdout).to.eql("");
      expect(stderr).to.eql("42\n");


      cleanups.push(tmp);
  });




  it("Should escape args", async () => {
      let tmp = new mock();

      let args = ["42", "", " ", "The sun is shinning", "Ho I' <", 'With a quoted " '];
      let child = spawn(tmp.execPath, ["-p", "JSON.stringify(process.argv.slice(1))", ...args]);
      let [exit, stdout, stderr] = await Promise.all([wait(child), drain(child.stdout), drain(child.stderr)]);
      stdout = String(stdout);
      stderr = String(stderr);

      expect(stdout).to.eql(JSON.stringify(args) + "\n");
      expect(stderr).to.eql("");

      cleanups.push(tmp);
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


      cleanups.push(tmp);
  });



  it("Should forward args", async () => {
      let tmp = new mock({ARGV4 : "JSON.stringify(process.argv.slice(1))", ARGV0 : '-p'});

      let args = ["42", "", " ", "The sun is shinning", "Ho I' <", 'With a quoted " '];
      let child = spawn(tmp.execPath, args);
      let [exit, stdout, stderr] = await Promise.all([wait(child), drain(child.stdout), drain(child.stderr)]);
      stdout = String(stdout);
      stderr = String(stderr);

      expect(stdout).to.eql(JSON.stringify(args) + "\n");
      expect(stderr).to.eql("");

      cleanups.push(tmp);
  });




  it("Should configure ENV", async () => {
      let ENV_FOOBAR = "ok boomer";
      let tmp = new mock({ARGV4 : "process.env.FOOBAR", ARGV0 : '-p', ENV_FOOBAR});

      let child = spawn(tmp.execPath);
      let [exit, stdout, stderr] = await Promise.all([wait(child), drain(child.stdout), drain(child.stderr)]);
      stdout = String(stdout);
      stderr = String(stderr);

      expect(stdout).to.eql(`${ENV_FOOBAR}\n`);
      expect(stderr).to.eql("");


      cleanups.push(tmp);
  });


  it("Should configure cwd", async () => {
      let tmp = new mock({CWD: "%dwd%"});

      let child = spawn(tmp.execPath, ["-p", "process.cwd()"]);
      let [exit, stdout, stderr] = await Promise.all([wait(child), drain(child.stdout), drain(child.stderr)]);
      stdout = String(stdout);
      stderr = String(stderr);

      expect(stdout).to.eql(`${tmp.wd}\n`);
      expect(stderr).to.eql("");

      cleanups.push(tmp);
  });


  it("Should configure output logs", async () => {
      let tmp = new mock({OUTPUT : "%dwd%\\test.log"});

      let child = spawn(tmp.execPath, ["-e", "console.error(42)"]);
      let [exit, stdout, stderr] = await Promise.all([wait(child), drain(child.stdout), drain(child.stderr)]);
      stdout = String(stdout);
      stderr = String(stderr);

      let challenge = fs.readFileSync(path.join(tmp.wd, 'test.log'), "utf-8");
      expect(stdout).to.eql("");
      expect(stderr).to.eql("");
      expect(challenge).to.eql("42\n");


      cleanups.push(tmp);
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

      let main = spawn(tmp.execPath, ['-e', "setInterval(a=>0, 1000)"]);

      console.log("Waiting for subchild", main.pid, "to appear"), await sleep(1000);

      let foo = await getProcessList();


      let dispatched = foo.find(line => line.ParentProcessId == main.pid)

      process.kill(dispatched.ProcessId);
      let end = await new Promise(resolve => main.on('exit', resolve));

      cleanups.push(tmp);
  });


  it("should kill child on dispatched exit (with job)", async () => {
      let tmp = new mock();

      let main = spawn(tmp.execPath,  ['-e', "setInterval(a=>0, 1000)"]);
      console.log("Waiting for subchild", main.pid, "to appear"), await sleep(1000);

      let before = await getProcessList();
      let dispatched = before.find(line => line.ParentProcessId == main.pid)
      main.kill();

      let after = await getProcessList();
      let dispatchedAfter = after.find(line => line.ProcessId == dispatched.processId)
      expect(dispatchedAfter).to.eql(undefined);

      cleanups.push(tmp);
  });



  it("should preserve child on dispatched exit (without job)", async () => {
      let tmp = new mock({USE_JOB : "false"});

      let main = spawn(tmp.execPath,  ['-e', "setInterval(a=>0, 1000)"]);
      console.log("Waiting for subchild", main.pid, "to appear"), await sleep(1000);

      let before = await getProcessList();
      let dispatched = before.find(line => line.ParentProcessId == main.pid)
      main.kill();

      let after = await getProcessList();
      let dispatchedAfter = after.find(line => line.ProcessId == dispatched.ProcessId)
      expect(dispatchedAfter).to.eql(dispatched);
      process.kill(dispatchedAfter.ProcessId);


      cleanups.push(tmp);
  });

});




let service = describe;

try {
  fs.utimesSync(process.env.SystemRoot, new Date(), new Date());
} catch(err) {
  if(err.code == 'EPERM') {
      console.log("Skipping suite as running as NON elevated process");
      service = describe.skip;
   }
}


service("Service tests suite", function() {

  let cleanups = [];
  this.timeout(60 * 1000);
  let port = 0;

  //debugger run as server, all services will act as client to PUSH infos
  let httpDefer = defer();
  let server = http.createServer(async (req, res) => {
    let payload = await drain(req);
    httpDefer.resolve(payload);
    res.end("ok");
  });

  server.listen(0, function() {
    port = this.address().port;
    console.log("Server running on port", port);
  });

  after("cleanup", async() => {
    for(let mock of cleanups) {
      await mock.cleanup();
      try {
        await wait(spawn("sc", ["stop", mock.name]));
      } catch (e) {};
      try {
        await wait(spawn("sc", ["delete", mock.name]));
      } catch (e) {};
    }
  });

  it("Be usable as nt service", async () => {
      
      let dict = {
        "AS_SERVICE" : true,
        "ARGV0" : '-e',
        "ARGV1" : `
    const http = require('http');
    const req = http.request({port:${port}, method:'POST'});
    req.end(JSON.stringify(process.env));
    setInterval(a=>0, 1000);
      `.replace(new RegExp("\\n", 'g'), ''),
      };

      let tmp = new mock(dict);
      cleanups.push(tmp);

      await wait(spawn("sc", ["create", tmp.name, "binPath=", tmp.execPath]));

      httpDefer = defer();
      await wait(spawn("sc", ["start", tmp.name]));
      let result = JSON.parse(await httpDefer);

      expect(result.USERPROFILE.toLowerCase()).to.eql('c:\\windows\\system32\\config\\systemprofile');
  });

  it("Be usable as user interactive service", async () => {
    if(!process.stdout.isTTY)
      return;

    let dict = {
        "AS_SERVICE" : true,
        "AS_DESKTOP_USER" : true,
        "ARGV0" : '-e',
        "ARGV1" : `
    const http = require('http');
    const req = http.request({port:${port}, method:'POST'});
    req.end(JSON.stringify(process.env));
    setInterval(a=>0, 1000);
      `.replace(new RegExp("\\n", 'g'), ''),
      };

      let tmp = new mock(dict);
      cleanups.push(tmp);

      await wait(spawn("sc", ["create", tmp.name, "binPath=", tmp.execPath]));

      httpDefer = defer();
      await wait(spawn("sc", ["start", tmp.name]));
      let result = JSON.parse(await httpDefer);
      expect(result.USERPROFILE).to.eql(process.env.USERPROFILE);
  });

});



