[![Build Status](https://github.com/131/dispatcher/actions/workflows/test.yml/badge.svg?branch=master)](https://github.com/131/dispatcher/actions/workflows/test.yml)
[![Version](https://img.shields.io/github/v/release/131/dispatcher)](https://github.com/131/dispatcher/releases)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](http://opensource.org/licenses/MIT)
![Available platform](https://img.shields.io/badge/platform-win32-blue.svg)




dispatcher
==========
Powerful process forwarder (or proxy) for Windows. It can be considered as a open source, free and more powerfull alternative to
[chocolatey shimgen](https://chocolatey.org/docs/features-shim)

# How to use
Download a dispatcher executable from Releases, or build it and choose one from
`output/`. Rename/duplicate it to `[something].exe`.
Write a `[something].config` file next to it to configure redirection.

Configuration file syntax is :
```
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <appSettings>
    <add key="PATH" value="[relative of full path to the exe you want to call]"/>
  </appSettings>
</configuration>
```

## Packaged desktop applications

Use `APP_NAME` instead of `PATH` for an installed desktop / FullTrust package:

```xml
<configuration>
  <appSettings>
    <add key="APP_NAME" value="OpenAI.Codex_2p2nqsd0c76g0!App"/>
    <add key="ENV_OPENAI_ENT_KEY" value="REPLACE_WITH_YOUR_KEY"/>
  </appSettings>
</configuration>
```

`APP_NAME` is the `PackageFamilyName!ApplicationId`, independent of the installed
version. `ENV_*`, `ARGV*`, additional command-line arguments and `CWD` work with
this mode. The target receives the dispatcher's complete environment, including
configured overrides. Without `CWD`, it starts in the dispatcher's current directory.
An application can change its own directory after startup; Codex currently does so.

`APP_NAME` runs entirely in C# using native Windows APIs. It resolves the installed
package and its manifest, then calls `IDesktopAppXActivator` through COM to start
the same dispatcher executable in helper mode inside the package context. The
helper creates the target with `CreateProcessW`; the original dispatcher monitors
the target. No PowerShell process, Appx module, or runtime script is required.

Environment values travel through a named pipe restricted to the current Windows
user, with network logons denied. The .NET Framework 4.8 transport uses
`NamedPipeServerStream` / `NamedPipeClientStream` and asynchronous I/O with
20-second operation deadlines. The parent checks the connecting process's PID
before sending the environment. It opens the target's process handle before
authorizing the helper to resume the suspended target; a cancelled or disconnected
launch terminates that suspended target.

The dispatcher allows concurrent launches of the same executable. The app
controls its own instance policy: if it redirects a launch to an existing process,
that process retains its original environment. For the tested Codex build, separate
`CODEX_HOME` and `--user-data-dir` values allowed two profiles to run simultaneously;
`CODEX_HOME` alone did not isolate the UI profile.

This mode currently supports the interactive Windows user; service and
alternate-user modes are rejected. The package bootstrap uses an internal Windows
COM interface, so compatibility with future Windows versions is not guaranteed.
Job assignment is best effort. `DETACHED=true` starts independently without waiting;
otherwise the dispatcher waits for the target and returns its exit code. The
bootstrap uses the activation options behind Windows' desktop-package diagnostic
launcher, whose context can differ from an ordinary Start-menu launch. PowerShell
is used only by the optional build/test scripts in `tests/packaged/`.

See [OFFLOAD.md](OFFLOAD.md) for implementation details and validation, and
[the packaged-launch tests](tests/packaged/run.ps1) for reproducible checks.

# Download
Find all downloads in [GitHub Releases](https://github.com/131/dispatcher/releases)

Dispatcher requires **.NET Framework 4.8 or later** on Windows. It uses the CLR 4
runtime; .NET Framework 2.0/3.5 is no longer required or supported.

## Building and testing

On a Windows machine with .NET Framework 4.8 or later, the build uses the compiler
already present in `C:\Windows\Microsoft.NET\Framework\v4.0.30319`.
Assembly lookup prefers an explicit `NET48_REFERENCE_ASSEMBLIES` directory, then
the installed Developer Pack's 4.8 reference DLLs, then the DLLs in the compiler's
directory if the pack is absent. An invalid explicit directory is an error.
No download, cache or environment setup is required. The packaged tests use the
same lookup order.

The executables declare a .NET Framework 4.8 target. When falling back to installed
runtime DLLs, the available APIs follow that Framework version. Building the
Visual Studio `.csproj` still uses the 4.8 targeting pack.

Build from Bash on Windows or WSL:

```bash
./build --build
```

The generated files are kept under `output/` (ignored by Git):

| Executable | Interface | Architecture |
| --- | --- | --- |
| `output/dispatcher_cmd.exe` | Console | x86 |
| `output/dispatcher_cmd_x64.exe` | Console | x64 |
| `output/dispatcher_win.exe` | GUI | x86 |
| `output/dispatcher_win_x64.exe` | GUI | x64 |

The build also prepares the autolock example in `output/examples/` from the GUI
x64 executable and `examples/autolock.exe.config`.

Run the tests from Windows PowerShell:

```powershell
.\tests\packaged\run.ps1
npm install
npm test
```

`npm test` uses the executables in `output/`; its temporary files go into
`output/tests/legacy/`. The packaged tests keep each run under
`output/tests/packaged/`. Visual Studio/MSBuild writes binaries to
`output/msbuild/<Configuration>/<Platform>/` and intermediate files to
`output/obj/<Configuration>/<Platform>/`.

The Bash build invokes `csc.exe` directly and produces console and GUI executables
for x86 and x64. `--sign` signs the four `output/dispatcher_*.exe` files, and
`--test` runs the existing test suite. The release workflow uploads those four
executables from `output/`, keeping their existing download filenames. The compiler's
`v4.0.30319` directory name is shared by .NET Framework 4.x, including 4.8;
the assembly metadata declares the 4.8 target.

The executables can still be renamed and configured as described above; no
additional runtime configuration file is needed to select CLR 4. If an existing
`.exe.config` contains a `<startup>` section that selects `v2.0.50727`, replace it
with `<startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup>`.


# Motivation  - sample usage
I use a lot of command line tool in windows (interpreters, encoders, git & related tools)
All of them need to be in my **`PATH`** (that I don’t like to change).
Merging them all in the same folder is a no go (.dll conflicts / overrides)

Using [dispatcher.exe](https://github.com/131/dispatcher) allows me to register only ONE directory in my Windows **`PATH`**, with very simple .exe forwarding processes to their genuine installation path.

```
# Current setup tree
C:\Program Files\node\bin\node.exe
C:\Program Files x86\php\bin\php.exe
D:\weird\directory\turtoisesvn\svn.exe
C:\cygwin\bin\git.exe


# I create  a single, well balanced directory
C:\dispatchedbin\

# I dispatch all binaries I want in it
C:\dispatchedbin\node.exe
C:\dispatchedbin\node.exe.config => D:\weird\directory\node-testing\node.exe

C:\dispatchedbin\php.exe
C:\dispatchedbin\php.exe.config => C:\Program Files x86\php\bin\php.exe
```

# Advanced usage, few things to understand
* There is a fundamental difference in console applications  & desktop applications for windows
* therefore [dispatcher](https://github.com/131/dispatcher) comes in 2 flavors - respectively dispatcher_cmd.exe &  dispatcher_win.exe.
* A 32-bit process can launch a 64-bit executable. On 64-bit Windows, WOW64 normally redirects a 32-bit process's `System32` accesses to `SysWOW64`; `Sysnative` provides access to the native system directory.
* Dispatcher is available in x86 and x64 builds. The native `APP_NAME` bootstrap has been tested with both on x64 Windows.

## Forced args
You can force additional args (injected before args that might have been sent toward `[dispatched].exe`
``` (node.exe.config)
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <appSettings>
    <add key="ARGV[XXX]" value="[optional argv 0 to XXX]"/>
  </appSettings>
</configuration>
```


## Env vars
You can define custom env var in dispatcher.config
``` (node.exe.config)
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <appSettings>
    <!-- Mandatory -->
    <add key="PATH" value="D:\apps\System32\bash.exe"/>
    <!-- All optionals -->
    <add key="ARGV0" value="-c"/>
    <add key="ARGV1" value="/usr/sbin/sshd -D"/>
    <add key="USE_SHOWWINDOW" value="true"/>
    <add key="CWD" value="c:\my\working\dir"/>

    <add key="ENV_FOO" value="bar"/>
    <add key="ENV_OTHERTHING" value="something"/>
  </appSettings>
</configuration>
```
## %dwd% macro
* `%dwd%` is replaced with the absolute path to the `[dispatched].exe` directory


## Multiple flavor
Using the env var `DISPATCHER_*NAME*_FLAVOR` you can toggle multiple flavor of an exe with the same dispatcher
```
node.config

<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <appSettings>

    <add key="PATH" value="..\node-v12.22.9-win-x64\node.exe"/>
    <add key="PATH_8" value="..\node-v8.17.0-win-x64\node.exe"/>
    <add key="PATH_16" value="..\node-v16.19.0-win-x64\node.exe"/>

    <add key="ENV_NODE_PATH" value="%dwd%/node_modules"/>
  </appSettings>
</configuration>


set DISPATCHER_NODE_FLAVOR=8 # will toggle node 8
set DISPATCHER_NODE_FLAVOR=16 # will toggle node 16

```


## DETACHED flag
Set `DETACHED=true` to return without waiting for the child to exit. This option is available in both console and GUI builds.

```
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <appSettings>
    <add key="PATH" value="putty.exe"/>
    <add key="DETACHED" value="true"/>
  </appSettings>
</configuration>
```


## spawn a command line app with no window (WSL bash.exe)
If you dispatch a console app (e.g. WSL bash.exe) from a desktop app (i.e. dispatch_win_x64.exe) you'll hide the window

```
# In my current configuration
D:\apps\wsl-init.exe (dispatch_win_x64.exe)
D:\apps\wsl-init.exe.config
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <appSettings>
    <add key="PATH" value="C:\Windows\System32\bash.exe"/>
    <add key="ARGV0" value="-c"/>
    <add key="ARGV1" value="/usr/sbin/sshd -D"/>
    <add key="USE_SHOWWINDOW" value="true"/>
  </appSettings>
</configuration>
```


## Pre-executation command
Using the `PRESTART_CMD` flag make **dispatcher** run a command before another (useful for services).


## Using dispatcher to run Windows service
Using the `AS_SERVICE` flag make **dispatcher** expose a Windows Service compliant interface. (therefore, you can use **dispatcher** to register any nodejs/php/whaterver script as a service. You'll have to manage the registration by yourself - see [sc create](https://docs.microsoft.com/en-us/windows-server/administration/windows-commands/sc-create),[sc start](https://docs.microsoft.com/en-us/windows-server/administration/windows-commands/sc-start), [sc stop](https://docs.microsoft.com/en-us/windows-server/administration/windows-commands/sc-stop), ... APIs). Also, if needed, you can run a service in an interactive session (interact with desktop - use [murrayju CreateProcessAsUser](https://github.com/murrayju/CreateProcessAsUser) ).

With `AS_SERVICE=auto`, the dispatcher selects service mode when running as LocalSystem, LocalService, or NetworkService. Service mode is supported by the `PATH` branch; `APP_NAME` rejects it.


```
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <appSettings>
    <add key="PATH" value="node.exe"/>
    <add key="ARGV0" value="main.js"/>
    <add key="AS_SERVICE" value="true"/>

<!-- prevent execution during UWF servicing sessions -->
    <add key="UWF_SERVICING_DISABLED" value="true"/>

<!-- to run a service in interactive session -->
    <add key="AS_DESKTOP_USER" value="true"/>

  </appSettings>
</configuration>
```

## UWF_SERVICING_DETECT
Using the `AS_SERVICE` or the `UWF_SERVICING_DETECT` flag will populate the `UWF_SERVICING_ENABLED` env variable with wether or not servicing mode is in progress.


## Redirect output to a file (usefull for services)
Using the `OUTPUT` flag redirect stderr & stdout to a dedicated file. Date modifiers are available.

```
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <appSettings>
    <add key="PATH" value="node.exe"/>
    <add key="ARGV0" value="main.js"/>
    <add key="OUTPUT" value="%temp%\logs-%Y%-%m%-%d% %H%-%i%-%s%.log"/>
  </appSettings>
</configuration>
```


# Service restart policy
In service mode, dispatcher will restart your process every time it exit, with an exponential (pow 2) backoff delay.


## SERVICE_RESTART_ON_NETWORK_CHANGE
Dispatcher can monitor network interface status change.
Use the `SERVICE_RESTART_ON_NETWORK_CHANGE` flag to reset the backoff delay.

```
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <appSettings>
    <add key="PATH" value="node.exe"/>
    <add key="ARGV0" value="main.js"/>
    <add key="AS_SERVICE" value="true"/>

    <add key="SERVICE_RESTART_ON_NETWORK_CHANGE" value="true"/>
  </appSettings>
</configuration>
```


## Configuration lookup path
dispatcher will lookup for configurations directives in

* if existing `[dispatched].config` (xml file)
* if existing `[dispatched].exe.config`  (xml file)
* all matching `[dispatched_directory]/[dispatched].config.d/*.config`  (xml files)

Any directive defined multipled time will be overrided with the latest value


## Using multiple versions of the same software
```
install php 5 in
C:\Program Files x86\php5.0\bin\php.exe
install php 7 in
C:\Program Files x86\php7.0\bin\php.exe

Create to dispatcher (php5.exe & php7.exe)
```

## Make a portable binary out of any shell/script
Using dispatcher.exe is a nifty way to create portable binaries out of shell scripts (.bat,.js,.php)


# How does it work
For `PATH`, the dispatcher creates the target through the Windows process APIs and
forwards its standard handles, which can refer to pipes, console streams, or files.
By default, the normal launch path uses a Windows job with kill-on-close behavior;
`USE_JOB=false` and `DETACHED` change process lifetime handling. When waiting for
the target, the dispatcher forwards its exit code.

For `APP_NAME`, the C# dispatcher uses native COM activation to start its helper
in the package context. The helper creates the target with its explicit
environment, arguments, and working directory. Job assignment is best effort.


# Running the command is slow
If you have a fresh install of Windows, you may have to build native images to [improve performance of managed applications](https://learn.microsoft.com/en-us/dotnet/framework/tools/ngen-exe-native-image-generator).

Open Command Prompt as an administrator and run these commands:
```
%windir%\Microsoft.NET\Framework\v4.0.30319\ngen.exe executeQueuedItems
%windir%\Microsoft.NET\Framework64\v4.0.30319\ngen.exe executeQueuedItems
```

If this does not solve the issue, it may be the application that Dispatcher is calling itself having slowdown issues.


## Tested & approved binaries (for reference)

* cmd apps : git (msysgit-1.8.4), php, node, python, svn, xpdf (pdftotext & ..), openssl, rsync, bash, gzip, tar, sed, ls, tee & co (from msysgit), ffmpeg, gsprint, 7z, ...
* desktop apps : nwjs, process explorer


# Credits
* [131](https://github.com/131)
* [murrayju](https://github.com/murrayju/CreateProcessAsUser)
* Code signing, courtesy of IVS Group.


# Relatives/alternatives
* [run.exe](http://www.straightrunning.com/projectrun.php) kinda stuff
* [shimgen](https://chocolatey.org/docs/features-shim)


# Shoutbox, keywords, SEO love
background cmd, wsl bash, linux subsystem, process forward, kernel32, USE_SHOWWINDOW
