using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace Utils
{
  public class Job : IDisposable
  {
    private IntPtr m_handle;
    private bool m_disposed = false;

    public Job()
    {
      m_handle = Kernel32.CreateJobObject(null, null);

      JOBOBJECT_BASIC_LIMIT_INFORMATION info = new JOBOBJECT_BASIC_LIMIT_INFORMATION();
      info.LimitFlags = 0x2000;

      JOBOBJECT_EXTENDED_LIMIT_INFORMATION extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
      extendedInfo.BasicLimitInformation = info;

      int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
      IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
      Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);

      if (!Kernel32.SetInformationJobObject(m_handle, JobObjectInfoType.ExtendedLimitInformation, extendedInfoPtr, (uint)length))
        throw new Exception(string.Format("Unable to set information.  Error: {0}", Marshal.GetLastWin32Error()));
    }

    #region IDisposable Members

    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    #endregion

    private void Dispose(bool disposing)
    {
      if (m_disposed)
        return;

      if (disposing) { }

      Close();
      m_disposed = true;
    }

    public void Close()
    {
      Kernel32.CloseHandle(m_handle);
      m_handle = IntPtr.Zero;
    }

    public bool AddProcess(IntPtr handle)
    {
      return Kernel32.AssignProcessToJobObject(m_handle, handle);
    }

  }


}
