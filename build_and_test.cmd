call "build.cmd"
if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%

call npm install
call npm ls
call npm test

if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%
