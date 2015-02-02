@set win=dispatcher_win.exe
@set exe=dispatcher_cmd.exe
del %win% %exe%
@set csc=C:\Windows\Microsoft.NET\Framework\v4.0.30319\Csc.exe
@set args=/noconfig /nowarn:1701,1702 /nostdlib+ /platform:x86 /errorreport:prompt /warn:0  /errorendlocation /preferreduilang:en-US /highentropyva- /reference:C:\Windows\Microsoft.NET\Framework\v2.0.50727\mscorlib.dll /reference:C:\Windows\Microsoft.NET\Framework\v2.0.50727\System.dll /filealign:512 /optimize+ /utf8output 
@set files=Utils\Job.cs Utils\Kernel32.cs Dispatcher\Program.cs Dispatcher\Properties\AssemblyInfo.cs
%csc%  %args% /out:%win%  /target:winexe %files%
%csc%  %args% /out:%exe%  /target:exe %files%


@set bundle_bin=D:\apps\bundle\bin

copy %exe% %bundle_bin%\php.exe
copy %exe% %bundle_bin%\svn.exe
copy %exe% %bundle_bin%\git.exe
copy %exe% %bundle_bin%\svn18.exe
copy %exe% %bundle_bin%\rsync.exe
copy %exe% %bundle_bin%\node.exe
copy %win% %bundle_bin%\nw.exe
