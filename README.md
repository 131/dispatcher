dispatcher
==========
Powerful process forwarder (or proxy) for Windows.

# How to use
Rename/duplicate `dispatcher.exe` to `[something].exe`.
Write a `[something].exe.config` file next to it to configure redirection.

Configuration file syntax is :
```
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <appSettings>
    <add key="PATH" value="[relative of full path to the exe you want to call]"/>
  </appSettings>
</configuration>
```


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

* You cannot spawn x64 executables located in c:\windows\system32 from a win32 application.
* therefore [dispatcher.exe](https://github.com/131/dispatcher) is available in 2 architectures : x32 & x64
* 
## Forced args
You can force additional args (injected before args what might have been send toward `[dispatched].exe`
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
    <add key="PATH" value="D:\apps\System32\bash.exe"/>
    <add key="ARGV0" value="-c"/>
    <add key="ARGV1" value="/usr/sbin/sshd -D"/>
    <add key="USE_SHOWWINDOW" value="true"/>
  </appSettings>
</configuration>
```
## %dwd% macro
* `%dwd%` is replaced with the absolute path to the `[dispatched].exe` directory



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


# How does it works
dispatcher use kernel32 Process spawn to force stdin, stdout & stderr handler to the forwarded process. Therefore, supports PIPE, Console or FILE as process handle (& all others handler). The dispatcher & the underlying process are bound to kernel32 Job group (tied together, you cannot kill one without the other). Exit code is forwarded.


## Tested & approved binaries (for reference)

* cmd apps : git (msysgit-1.8.4), php, node, python, svn, xpdf (pdftotext & ..), openssl, rsync, bash, gzip, tar, sed, ls, tee & co (from msysgit), ffmpeg, gsprint, 7z, ...
* desktop apps : nwjs, process explorer


# Credits
* [131](https://github.com/131)



# Shoutbox, keywords, SEO love
background cmd, wsl bash, linux subsystem, process forward, kernel32, USE_SHOWWINDOW