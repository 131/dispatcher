# Unreleased

* Put binaries, examples and test/MSBuild artifacts under `output/`; update signing and release uploads accordingly.
* Require .NET Framework 4.8 or later; remove the .NET Framework 2.0/3.5 dependency.
* Rewrite package IPC using managed named pipes and async/await, retaining the existing launch protocol, environment transfer, PID checks and suspended-start handshake.
* Prefer .NET Framework 4.8 reference assemblies, falling back to installed runtime DLLs when the Developer Pack is absent.
* Update Windows CI and rebuild the bundled autolock example for .NET Framework 4.8.

# v2.2.5
* Add a 10s watchdog in UWF_SERVICING_DETECT

# v2.2.3
* Fix win32 logs output

# v2.2.2
* Expose UWF_SERVICING_ENABLED through UWF_SERVICING_DETECT or AS_SERVICE flag

# v2.2.0
* Allow multiple flavor dispatch using DISPATCHER_*NAME*_FLAVOR=XXX syntax


# v2.0.3
* Expose DISPATCHED_SERVICE_MODE=true when running as service = auto



# v2.0.2
* Signed releases using github actions

# v2.0.0
* Now with signed release

# v1.29.0
* Allow AS_DESKTOP_USER auto value

# v1.28.0
* Allow AS_SERVICE auto value



# v1.1.0
* Prevent comma "," to be quote encoded in args (fix for user32.dll calls)
* Add examples folder



# v1.0.0
* Initial release
