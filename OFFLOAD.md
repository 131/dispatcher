# APP_NAME implementation handoff

Updated September 18, 2026, after migrating to .NET Framework 4.8 and rewriting
the package transport with managed named pipes and async/await. Package lookup
and activation still use native Windows APIs and COM.

## Current status

- `APP_NAME` support is implemented entirely in C#, compiled, and tested.
  **No PowerShell process, Appx module, or runtime script is required.** Native
  app-model APIs resolve the package; direct COM activation enters its context.
  The C# helper handles environment transfer and target creation.
- The initial check that rejected another process with the same executable path
  has been removed. Do not restore it: instance policy belongs to the application.
- On the tested Codex version, `CODEX_HOME` plus `--user-data-dir` allows two
  profiles to run concurrently. The extra `CODEX_ELECTRON_USER_DATA_PATH` variable
  was removed after verifying that the personal app and its app-server worked
  without it.
- The installed launchers are now `chatgpt-el.exe` and `chatgpt-perso.exe`. Their
  current settings are recorded below; earlier launcher names are obsolete.
- The user's priorities are ENV and ARGV. CWD is supported and tested but is not
  important for this use case. Jobs and advanced modes are secondary.
- Vauth integration is deferred. Changes are local and uncommitted; nothing has
  been signed or published.

## Configuration

`APP_NAME` is an alternative to `PATH`:

```xml
<configuration>
  <appSettings>
    <add key="APP_NAME" value="OpenAI.Codex_2p2nqsd0c76g0!App"/>
    <add key="ENV_OPENAI_ENT_KEY" value="REPLACE_WITH_YOUR_KEY"/>
  </appSettings>
</configuration>
```

See [examples/codex-app.exe.config](examples/codex-app.exe.config).
Actual credentials stay in the local configuration, not in examples or this file.

The identifier is `PackageFamilyName!ApplicationId`; it does not contain the
installed package version. The dispatcher rejects configurations that select both
`APP_NAME` and `PATH`.

## Native C# launch sequence

1. `src/Program.cs` reads the configuration, applies the existing `ENV_*` and
   `ARGV*` rules, appends launcher arguments, and selects `PATH` or `APP_NAME`.
   Environment variable names are case-insensitive.
2. For `APP_NAME`, `ResolveExecutable` in `src/Utils/PackagedApplication.cs` calls
   `GetPackagesByPackageFamily` and `GetPackagePathByFullName`, then reads
   `AppxManifest.xml` with `XmlDocument`. It selects the matching desktop app in
   the highest registered manifest version. `StartInPackage` directly calls
   `IDesktopAppXActivator::ActivateWithOptions` to start the same dispatcher
   executable with `--dispatcher-package-helper` inside the package context.
3. The parent C# process sends the complete target environment, arguments, and CWD
   to that helper through a managed named pipe restricted to the current Windows
   user, denying network logons. Secret values are not placed in command-line
   arguments or temporary files.
4. The C# helper calls `CreateProcessW` with an explicit Unicode environment block
   and creates the target suspended. Its own environment is not implicitly merged
   into the target environment.
5. The parent opens the target's process handle before authorizing `ResumeThread`.
   It waits for the actual target and returns its exit code unless `DETACHED` is set.

This is a launch sequence, not a literal process-parent tree:

```text
C# dispatcher -- native COM --> C# helper in package context --> target
      |                              ^
      +--- environment/args/CWD -----+  (private pipe)
```

Both branches run without PowerShell. The optional build and inspection scripts
in `tests/packaged/` use PowerShell as a test harness, not as a runtime dependency.
The parent also verifies that the pipe client PID matches the process returned
by native activation before sending the environment.

Without `CWD`, the target receives the dispatcher's current directory. Relative
`CWD` values are resolved from that directory. An application may change its own
working directory after startup; the tested Codex build does this.

The project now targets .NET Framework 4.8. The Bash `build` script invokes
`csc.exe` directly; the PowerShell packaged-test harness compiles its own probes.
Both prefer `NET48_REFERENCE_ASSEMBLIES` when explicitly set, then the installed
Developer Pack's 4.8 references, then the runtime DLLs in
`C:\Windows\Microsoft.NET\Framework\v4.0.30319` if the pack is absent.
An invalid explicit override fails. No download or setup is required. Assembly
metadata declares 4.8; fallback builds expose the installed Framework's APIs.
`PackagePipe.cs` owns managed pipe creation, protected ACLs,
connection monitoring and asynchronous reads/writes with 20-second deadlines.
The original BinaryReader/BinaryWriter protocol is preserved, including the
inspection tools. The remaining pipe P/Invoke checks the connected client's PID
before any environment data is sent. The source is also included in
`src/Dispatcher.csproj`.

## Scope and limitations

- Packaged desktop / FullTrust applications, tested with Codex. This is not a
  claim of general UWP support.
- The bootstrap uses the internal `IDesktopAppXActivator` COM interface. Its ABI
  is not a public Windows compatibility contract and may change. The implementation
  has been validated in x86 and x64 on this Windows installation.
- `APP_NAME` rejects service and alternate-user modes (`AS_SERVICE`, `AS_USER`,
  `AS_DESKTOP_USER`, and service network-restart behavior). Supporting them would
  require package resolution and activation for the appropriate identity.
- Job assignment is attempted before target creation. If assignment fails, the
  dispatcher reports it and continues. `DETACHED` skips that job and does not
  retain the parent's standard streams.
- `OUTPUT` and target exit-code forwarding have been tested. Advanced option
  combinations do not have exhaustive coverage.
- Concurrent launches are allowed. If the application redirects a new launch to
  an existing instance, that instance keeps its original environment. `CODEX_HOME`
  by itself does not necessarily isolate the application's UI profile.
- Activation uses the options behind `Invoke-CommandInDesktopPackage` with
  `PreventBreakaway`, without invoking that cmdlet. Its diagnostic activation
  context can differ from normal app activation.

## Validation completed

- The managed transport passes x86/x64 tests for connection timeouts, early helper
  exit, stalled and partial reads, blocked writes, disconnection, fragmented
  messages, Unicode and malformed string lengths.
- Cancellation and parent disconnection before resume terminate the suspended
  target without executing it. Detached targets survive the launcher's return.
  These checks pass both normally and in the installed Codex package context.
  A client with the wrong PID is rejected before receiving any data.
- Rebuilt all four variants (console and GUI, x86 and x64) after the managed
  transport rewrite. The Release/x64 `.csproj` build also passes.
- Native package resolution and COM activation pass in x86 and x64; no Appx
  cmdlet participates in the package-context transport tests.
- Exercised transport and target creation in x86/x64, both in an ordinary process
  context and in the actual Codex package context: inherited environment, values
  containing Unicode/quotes/newlines, exclusion of a helper-only variable, empty
  arguments, spaces, quotes, trailing backslashes, Unicode, a distinct CWD,
  `OUTPUT`, and the actual target exit code.
- Added a concurrent-launch regression check: one probe executable remains active
  while a second instance of that same executable starts. It passes in x86/x64
  and ordinary/package contexts after removal of `IsRunning`.
- Checked invalid configuration errors: malformed AUMID, `APP_NAME` with `PATH`,
  unsupported user mode, and a missing package. Each fails with a nonzero exit
  code and a useful message.
- All 13 existing initial/job tests pass for the `PATH` branch, arguments,
  environment, CWD, redirection and process lifetime. The two service tests are
  skipped by the suite because this session is not elevated. The local npm launcher
  is broken, so the installed Mocha entry point was run directly with Windows Node.
- Inspected the real packaged `ChatGPT.exe` while it was still suspended:
  **ENV, ARGV, and CWD matched before any application code executed.**
  Repeated after the native conversion using both local profile configurations,
  an x64 console helper and an x86 GUI helper; configured ENV overrides matched.
  Rechecked after the managed rewrite using the rebuilt x64 GUI dispatcher and
  the public example configuration with the existing inspection script.
- After startup, Codex retained ENV/ARGV but changed CWD to its installation
  directory. That change comes from application startup, not the dispatcher.

Run the reproducible checks from Windows PowerShell:

```powershell
.\tests\packaged\run.ps1
# Also run probes in the package context, without opening the Codex UI:
.\tests\packaged\run.ps1 -PackageAppName 'OpenAI.Codex_2p2nqsd0c76g0!App'
```

`InspectCreatedApplication.ps1` calls the production C# resolver and COM activator
through reflection, inspects the real target before `ResumeThread`, then cancels
creation so no application code runs. It checks configured environment overrides
without printing their values, plus a synthetic Unicode argument and CWD. `InspectProcess.ps1` compares
configured values with a running process without printing secret values. The
memory inspection scripts target x64 processes and require x64 PowerShell.

## Local installation record (September 17)

The September 18 .NET 4.8 builds are in `output/`; the build prepares the autolock
example in `output/examples/`. Test builds and results are under `output/tests/`;
MSBuild outputs and intermediates are under `output/msbuild/` and `output/obj/`.
Signing and release uploads use `output/dispatcher_*.exe`.
Pre-existing `src/bin/` and `src/obj/` artifacts were moved to
`output/legacy-msbuild/`.
The installation record below describes the preceding
deployment of the local profile launchers.

Directory: `D:\apps\bundle\bin`. These configurations were read during the
preceding handoff update:

| Profile | Launcher | Configuration |
|---|---|---|
| Enterprise | `chatgpt-el.exe` | `chatgpt-el.config` |
| Personal | `chatgpt-perso.exe` | `chatgpt-perso.config` |

### Enterprise: chatgpt-el.config

```xml
<configuration>
  <appSettings>
    <add key="APP_NAME" value="OpenAI.Codex_2p2nqsd0c76g0!App"/>
    <add key="ENV_OPENAI_ENT_KEY" value="REPLACE_WITH_YOUR_EXISTING_KEY"/>
    <add key="ENV_CODEX_HOME" value="C:\Users\fleurent\OneDrive - Luxottica Group S.p.A\configs\codex-el"/>
    <add key="ARGV0" value="--user-data-dir=C:\Users\fleurent\AppData\Roaming\Codex-el"/>
  </appSettings>
</configuration>
```

### Personal: chatgpt-perso.config

```xml
<configuration>
  <appSettings>
    <add key="APP_NAME" value="OpenAI.Codex_2p2nqsd0c76g0!App"/>
    <add key="ENV_CODEX_HOME" value="D:\131\configs\codex-perso"/>
    <add key="ARGV0" value="--user-data-dir=C:\Users\fleurent\AppData\Roaming\Codex-perso"/>
  </appSettings>
</configuration>
```

The personal configuration has no explicit enterprise key. Both configurations
use `--user-data-dir`, and neither defines `ENV_CODEX_ELECTRON_USER_DATA_PATH`.
Values containing spaces remain ordinary XML values; the dispatcher handles
argument encoding.

The earlier `chatgpt.exe` / `chatgpt.config` and `codex-perso.exe` /
`codex-perso.config` are no longer installed under those names. The external
prototype script was removed. All four local repository binaries were rebuilt;
no signing or publishing was performed.

Both installed launchers now match the tested native x86 GUI build by SHA-256.
Their configuration files were preserved byte for byte. The build output and
copies of the previous launchers are in
`C:\Users\fleurent\AppData\Local\Temp\dispatcher-native-com-build`.

**Current settings versus earlier tests:** the newer `CODEX_HOME` paths above
were verified in suspended target environments after the native conversion.
The running apps were not restarted, and those directories were not inspected.
The concurrent UI/app-server checks below were done earlier, including with
`C:\Users\fleurent\.codex-perso` as the personal home. The user had backed up
that original directory.

## Why standard activation was insufficient

On this installation, direct execution outside the package context returned
Win32 error 5 (access denied). `shell:AppsFolder` opened the app without transferring
the dispatcher's environment. `IApplicationActivationManager` returns a PID but
accepts neither an environment block nor a working directory.

An `IPackageDebugSettings::EnableDebugging` prototype with an environment block
failed with `0x80070057`; the cause was not established. Activation without that
block succeeded, and `DisableDebugging` then succeeded. The current implementation
does not use package debugging settings.

The implementation now calls the cmdlet's underlying internal COM interface directly:

- CLSID: `168EB462-775F-42AE-9111-D714B2306C2E`.
- IID: `F158268A-D5A5-45CE-99CF-00D6C3F3FC0A`, `InterfaceIsIUnknown`.
- First slots: `Activate`, then `ActivateWithOptions`.
- `ActivateWithOptions` receives options `4 | 16 | 32` (52) and parent PID 0,
  matching the Windows cmdlet's `PreventBreakaway` path. The bits request a
  nonpackaged executable process tree, an app-installer update check, and
  Centennial process activation.
- The returned process handle is converted to a PID with `GetProcessId` and closed.
  The COM object is released after activation.

The ABI and flags were checked against the installed Microsoft Appx cmdlet's
metadata and IL, then validated with actual x86/x64 package-context launches.
This remains an internal interface, isolated in `PackagedApplication.cs`.

## Two concurrent Codex profiles: tested behavior

On `OpenAI.Codex_26.911.7940.0_x64__2p2nqsd0c76g0`, the first successful test used
both `CODEX_ELECTRON_USER_DATA_PATH` and `--user-data-dir`. A subsequent test
confirmed that **the argument alone separates the UI profile on this version**,
while retaining `CODEX_HOME` for Codex data:

```xml
<add key="ENV_CODEX_HOME" value="C:\Users\fleurent\.codex-perso"/>
<add key="ARGV0" value="--user-data-dir=C:\Users\fleurent\AppData\Roaming\Codex-perso"/>
```

`ENV_CODEX_ELECTRON_USER_DATA_PATH` was therefore removed from the personal
configuration, then named `D:\apps\bundle\bin\codex-perso.config` and subsequently
renamed `chatgpt-perso.config`. After restarting the personal profile while the
enterprise profile was already open, checks confirmed:

- Two windows and two distinct main application processes.
- No `CODEX_ELECTRON_USER_DATA_PATH` variable in the personal process.
- The requested `--user-data-dir` argument and matching personal Crashpad directory.
- The expected `CODEX_HOME` in both the UI process and its `codex.exe app-server`.

The app also implements `CODEX_ELECTRON_USER_DATA_PATH`, which selects the Electron
path and explicitly preserves `CODEX_HOME` when reloading the shell environment.
That extra protection was not needed in the Windows test. These findings apply
to the tested version and environment, not necessarily every startup mode or
future release.

## Environment provider / vauth

`ENV_PROVIDER` is implemented for both `PATH` and `APP_NAME`. It accepts one
Windows command line, runs it before target creation, and requires one JSON
object whose keys and values are strings. The provider has a five-second default
timeout, configurable in seconds through `ENV_PROVIDER_TIMEOUT` and a 16 MiB output limit. A nonzero exit code, timeout, or invalid JSON
stops the launch. Provider values are never logged.

The provider receives the inherited environment plus explicit `ENV_*` values.
The final merge order is inherited environment -> provider JSON -> explicit
`ENV_*` overrides. `ENV_PROVIDER_CWD` defaults to the current working directory and
supports `%dwd%`. A deployed vvauth can be used without storing a Vault token in
the launcher config:

```xml
<add key="ENV_PROVIDER" value="wsl.exe -e vauth env --ir://json"/>
<add key="ENV_PROVIDER_CWD" value="%dwd%"/>
<add key="ENV_VAUTHRC" value="chatgpt-foundry.vauthrc"/>
```

Here `ENV_VAUTHRC` is visible to `wsl.exe`; WSL interop maps the relative value
against `ENV_PROVIDER_CWD`, so vvauth resolves the file beside the launcher.

## Repository files for follow-up work

- `src/Utils/PackagePipe.cs`: managed asynchronous pipe transport and protocol encoding.
- `src/Utils/PackagedApplication.cs`: native package lookup, COM bootstrap, environment block,
  suspended creation, resume, and target monitoring; no existing-executable filter.
- `src/Program.cs`: `APP_NAME` configuration and launch routing.
- `src/Dispatcher.csproj` and `build`: source inclusion.
- `tests/packaged/`: ENV/ARGV/CWD and concurrency tests, invalid configurations,
  probes, and inspection tools; `run.ps1` builds all four variants.
- `README.md` and `examples/codex-app.exe.config`: public documentation and a
  generic example without real credentials. This file is the handoff record.

The native C# conversion and `ENV_PROVIDER` integration are complete locally.
The Foundry launcher now negotiates its environment at each launch.

## References

- https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iapplicationactivationmanager-activateapplication
- https://learn.microsoft.com/en-us/powershell/module/appx/invoke-commandindesktoppackage
