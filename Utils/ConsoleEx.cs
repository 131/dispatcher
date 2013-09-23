using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace Utils
{
  public static class ConsoleEx
  {
    public static bool IsOutputRedirected
    {
      get { return FileType.Char != GetFileType(GetStdHandle(StdHandle.Stdout)); }
    }
    public static bool IsInputRedirected
    {
      get { return FileType.Char != GetFileType(GetStdHandle(StdHandle.Stdin)); }
    }
    public static bool IsErrorRedirected
    {
      get { return FileType.Char != GetFileType(GetStdHandle(StdHandle.Stderr)); }
    }

    // P/Invoke:
    private enum FileType { Unknown, Disk, Char, Pipe };
    private enum StdHandle { Stdin = -10, Stdout = -11, Stderr = -12 };
    [DllImport("kernel32.dll")]
    private static extern FileType GetFileType(IntPtr hdl);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(StdHandle std);


    [DllImport("kernel32.dll")]
    public static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32")]
    public static extern bool AllocConsole();

  }
}
