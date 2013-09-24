using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;

namespace Utils
{


  public enum ChatMethod
  {
    CopyTo,
    Async,
    Sync,
  }

  public class Chat
  {

    private byte[] Buffer { get; set; }
    private Stream rStream { get; set; }
    private Stream wStream { get; set; }
    public bool EOF = false;
    public string Name = "none";
    public bool Alive { get { return mThread.IsAlive; } }

    Thread mThread;
    ChatMethod mMethod = ChatMethod.CopyTo;


    private Chat(Stream read, Stream write, string name)
    {
      this.Buffer = new byte[1024];
      this.rStream = read;
      this.wStream = write;
      this.Name = name;
    }

    void RunCopy()
    {
#if NET4
      rStream.CopyTo(wStream);
#else
      RunASync();
#endif
    }

    void RunASync()
    {
      rStream.BeginRead(Buffer, 0, Buffer.Length, new AsyncCallback(sRead), this);
    }


    void RunSync()
    {
      do
      {
        int read = rStream.Read(Buffer, 0, Buffer.Length);
        if (read != 0)
        {
          wStream.Write(Buffer, 0, read);
          wStream.Flush();
        }
        else if (mend) break;
        //if ((new StreamReader(rStream)).EndOfStream) break;
      } while (rStream.CanRead);

      this.Close();
    }

    public static Chat Start(Stream read, Stream write, ChatMethod method, string Name)
    {
      Chat c = new Chat(read, write, Name);
      c.mMethod = method;
      c.Start();
      return c;
    }

    public void Start()
    {
      if (mThread == null)
      {
        if (mMethod == ChatMethod.Async)
          mThread = new Thread(RunASync);

        if (mMethod == ChatMethod.CopyTo)
          mThread = new Thread(RunCopy);

        if (mMethod == ChatMethod.Sync)
          mThread = new Thread(RunSync);
      }
      mThread.Name = Name;
      mThread.Start();
    }

    static void sRead(IAsyncResult async)
    {
      Chat state = (Chat)async.AsyncState;
      int dataLen = state.rStream.EndRead(async);

      if (dataLen == 0)
      {
        state.EOF = true;
        return;
      }

      string str = UTF8Encoding.UTF8.GetString(state.Buffer, 0, dataLen);
      state.wStream.Write(state.Buffer, 0, dataLen);
      state.wStream.Flush();
      state.rStream.BeginRead(state.Buffer, 0, state.Buffer.Length, new AsyncCallback(sRead), state);
    }


    internal void Close()
    {
      mThread.Abort();
      rStream.Close();
      wStream.Close();
    }

    private bool mend = false;
    internal void End()
    {
      mend = true;
    }
  }

}
