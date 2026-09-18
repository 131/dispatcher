using System;
using System.Text;
using System.Runtime.InteropServices;
public static class DispatcherProcessInspector {
 [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
 [DllImport("kernel32.dll", SetLastError=true)] static extern bool ReadProcessMemory(IntPtr p, IntPtr address, byte[] data, IntPtr size, out IntPtr read);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
 [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr p, int info, IntPtr[] data, int size, out int returned);
 [DllImport("shell32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr CommandLineToArgvW(string command, out int argc);
 [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
 static byte[] Read(IntPtr p, long address, int count) { byte[] b=new byte[count]; IntPtr n; if(!ReadProcessMemory(p,new IntPtr(address),b,new IntPtr(count),out n)) throw new System.ComponentModel.Win32Exception(); return b; }
 static long Parameters(IntPtr p) { IntPtr[] info=new IntPtr[6]; int n; if(NtQueryInformationProcess(p,0,info,48,out n)!=0) throw new Exception("Cannot read process information"); return BitConverter.ToInt64(Read(p,info[1].ToInt64()+0x20,8),0); }
 public static string ReadValue(int pid, string name) {
  if(IntPtr.Size!=8) throw new Exception("Use 64-bit PowerShell for the x64 target");
  IntPtr p=OpenProcess(0x410,false,pid); if(p==IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
  try {
   long parameters=Parameters(p);
   if(name=="@CWD" || name=="@ARGV") {
    long descriptor=parameters+(name=="@CWD" ? 0x38 : 0x70);
    int length=BitConverter.ToUInt16(Read(p,descriptor,2),0);
    long address=BitConverter.ToInt64(Read(p,descriptor+8,8),0);
    return Encoding.Unicode.GetString(Read(p,address,length));
   }
   long env=BitConverter.ToInt64(Read(p,parameters+0x80,8),0);
   StringBuilder value=new StringBuilder();
   for(int i=0;i<1048576;i+=2) {
    char c=(char)BitConverter.ToUInt16(Read(p,env+i,2),0);
    if(c=='\0') { if(value.Length==0) return null; string entry=value.ToString(); if(entry.StartsWith(name+"=",StringComparison.OrdinalIgnoreCase)) return entry.Substring(name.Length+1); value.Length=0; }
    else value.Append(c);
   }
   throw new Exception("Environment exceeds inspection limit");
  } finally { CloseHandle(p); }
 }
 public static string[] Arguments(int pid) {
  int count; IntPtr memory=CommandLineToArgvW(ReadValue(pid,"@ARGV"),out count);
  if(memory==IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
  try { string[] result=new string[count]; for(int i=0;i<count;i++) result[i]=Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory,i*IntPtr.Size)); return result; }
  finally { LocalFree(memory); }
 }
}
