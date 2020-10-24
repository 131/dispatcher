@set win=dispatcher_win.exe
@set exe=dispatcher_cmd.exe
@set win_64=dispatcher_win_x64.exe
@set exe_64=dispatcher_cmd_x64.exe

@set win_elevated=dispatcher_win_uac.exe
@set exe_elevated=dispatcher_cmd_uac.exe
@set win_64_elevated=dispatcher_win_x64_uac.exe
@set exe_64_elevated=dispatcher_cmd_x64_uac.exe


@set elevate=/win32manifest:Dispatcher\elevate.manifest
del %win% %exe% %win_64% %exe_64% %win_elevated% %exe_elevated% %win_64_elevated% %exe_64_elevated%

@set csc=Csc.exe
@set args=/noconfig /nowarn:1701,1702 /nostdlib+ /errorreport:prompt /warn:0  /errorendlocation /preferreduilang:en-US /highentropyva- /reference:C:\Windows\Microsoft.NET\Framework\v2.0.50727\mscorlib.dll /reference:C:\Windows\Microsoft.NET\Framework\v2.0.50727\System.dll /reference:C:\Windows\Microsoft.NET\Framework\v2.0.50727\System.Xml.dll /reference:C:\Windows\Microsoft.NET\Framework\v2.0.50727\System.ServiceProcess.dll /filealign:512 /utf8output 
@set files=Dispatcher\Utils\Job.cs Dispatcher\Utils\Kernel32.cs Dispatcher\Program.cs Dispatcher\Properties\AssemblyInfo.cs Dispatcher\ProcessExtensions.cs



%csc%  %args% /platform:x86 /define:DISPACHER_WIN /out:%win%  /target:winexe %files%
%csc%  %args% /platform:x86 /out:%exe%  /target:exe %files%

%csc%  %args% /platform:x64 /define:DISPACHER_WIN /out:%win_64%  /target:winexe %files%
%csc%  %args% /platform:x64 /out:%exe_64%  /target:exe %files%


dir


REM %csc%  %args% %elevate% /platform:x86 /define:DISPACHER_WIN /out:%win_elevated%  /target:winexe %files%
REM %csc%  %args% %elevate% /platform:x86 /out:%exe_elevated%  /target:exe %files%

REM %csc%  %args% %elevate% /platform:x64 /define:DISPACHER_WIN /out:%win_64_elevated%  /target:winexe %files%
REM %csc%  %args% %elevate% /platform:x64 /out:%exe_64_elevated%  /target:exe %files%



REM call deploy.bat %win% %exe% %win_64% %exe_64%

