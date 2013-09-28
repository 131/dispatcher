dispatcher
==========

Process forwarder (stdin/stdout/stderr)

Rename dispatcher.exe to php.exe (for instance) and create a txt file named "dispatch" near to it.

The "dispatch" file is a line based per-exe process redirection manager (based on the name you renamed dispatcher.exe ).

syntax is : 
(dispatchedname) (path_to_real_exe) (extra args)

* path_to_real_exe is relative to the dispatched.exe (or absolute)..
* Process environnement is extended with path_to_real_exe directory.
* %dwd% is replaced by absolute path to the dispatched.exe directory


== Dispatch file sample ==

php ..\php-54-nts\php.exe -d include_path="%dwd%\..\libraries" -c "%dwd%\..\conf\php-cli.ini"
svn ..\svn-1.6.18\svn.exe
svn18 ..\svn-1.8.3\svn.exe
rsync ..\rsync\rsync.exe
git ..\msysgit-1.8.4\bin\git.exe
node ..\node-0.10.4\node.exe
7z ..\7z-920\7z.exe



