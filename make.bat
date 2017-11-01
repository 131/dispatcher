@echo off
@set win=dispatcher_win.exe
@set exe=dispatcher_cmd.exe
@set win_64=dispatcher_win_64.exe
@set exe_64=dispatcher_cmd_64.exe
del %win% %exe% %win_64% %exe_64%

@set csc=C:\Windows\Microsoft.NET\Framework\v4.0.30319\Csc.exe
@set args=/noconfig /nowarn:1701,1702 /nostdlib+ /errorreport:prompt /warn:0  /errorendlocation /preferreduilang:en-US /highentropyva- /reference:C:\Windows\Microsoft.NET\Framework\v2.0.50727\mscorlib.dll /reference:C:\Windows\Microsoft.NET\Framework\v2.0.50727\System.dll /reference:C:\Windows\Microsoft.NET\Framework\v2.0.50727\System.Configuration.dll /filealign:512 /utf8output 
@set files=Dispatcher\Utils\Job.cs Dispatcher\Utils\Kernel32.cs Dispatcher\Program.cs Dispatcher\Properties\AssemblyInfo.cs



%csc%  %args% /platform:x86 /define:DISPACHER_WIN /out:%win%  /target:winexe %files%
%csc%  %args% /platform:x86 /out:%exe%  /target:exe %files%

%csc%  %args% /platform:x64 /define:DISPACHER_WIN /out:%win_64%  /target:winexe %files%
%csc%  %args% /platform:x64 /out:%exe_64%  /target:exe %files%



REM call deploy.bat %win% %exe% %win_64% %exe_64%

