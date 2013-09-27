
////////////////////////////////////////////////////////////////////// 
// child.c
// Echoes all input to stdout. This will be redirected by the redirect
// sample. Compile and build child.c as a Win32 Console application and
// put it in the same directory as the redirect sample.
// 
#include<windows.h>
#include<stdio.h>
#include<string.h>

void main ()
{
  FILE*    fp;
  CHAR     szInput[1024];


  // Open the console. By doing this, you can send output directly to
  // the console that will not be redirected.

  fp = fopen("CON", "w");
  if (!fp) {
     printf("Error opening child console - perhaps there is none.\n");
     fflush(NULL);
     return;
  }
  else
  {

  // Write a message direct to the console (will not be redirected).

     fprintf(fp,"This data is being printed directly to the\n");
     fprintf(fp,"console and will not be redirected.\n\n");
     fprintf(fp,"Since the standard input and output have been\n");
     fprintf(fp,"redirected data sent to and from those handles\n");
     fprintf(fp,"will be redirected.\n\n");
     fprintf(fp,"To send data to the std input of this process.\n");
     fprintf(fp,"Click on the console window of the parent process\n");
     fprintf(fp,"(redirect), and enter data from it's console\n\n");
     fprintf(fp,"To exit this process send the string 'exit' to\n");
     fprintf(fp,"it's standard input\n");
     fflush(fp);
  }

  ZeroMemory(szInput,1024);
  while (TRUE)
  {
     gets(szInput);
     printf("Child echoing [%s]\n",szInput);
     fflush(NULL);  // Must flush output buffers or else redirection
                    // will be problematic.
     if (!_stricmp(szInput,"Exit") )
        break;

     ZeroMemory(szInput,strlen(szInput) );

  }
}