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
  }

  public class Chat
  {

    Stream mRead;
    Stream mWrite;
    Thread mThread;
    ChatMethod mMethod = ChatMethod.CopyTo;

    string Name { get; set; }

    public Chat(Stream read, Stream write)
    {
      Name = "meta";
      mWrite = write; mRead = read;

    }

    void RunCopy()
    {
#if NET4
      mRead.CopyTo(mWrite);
#else
      RunASync();
#endif
    }

    void RunASync()
    {
      byte[] rbuffer = new byte[1024];
      mRead.BeginRead(rbuffer, 0, rbuffer.Length, new AsyncCallback(sRead),
        new StreamState { Buffer = rbuffer, rStream = mRead, wStream = mWrite });
    }

    public static void Start(Stream read, Stream write, ChatMethod method, string Name)
    {
      Chat c = new Chat(read, write);
      c.Name = Name;
      c.mMethod = method;
      c.Start();
    }

    public void Start()
    {
      if (mThread == null)
      {
        if (mMethod == ChatMethod.Async)
          mThread = new Thread(RunASync);

        if (mMethod == ChatMethod.CopyTo)
          mThread = new Thread(RunCopy);
      }

      mThread.Start();
    }

    static void sRead(IAsyncResult async)
    {
      StreamState state = (StreamState)async.AsyncState;
      int dataLen = state.rStream.EndRead(async);

      if (dataLen == 0)
        state.EOF = true;


      string str = UTF8Encoding.UTF8.GetString(state.Buffer, 0, dataLen);
      state.wStream.Write(state.Buffer, 0, dataLen);
      state.wStream.Flush();
      state.rStream.BeginRead(state.Buffer, 0, state.Buffer.Length, new AsyncCallback(sRead), state);
    }

  }

  public class StreamState
  {
    public byte[] Buffer { get; set; }
    public Stream rStream { get; set; }
    public Stream wStream { get; set; }
    public bool EOF = false;
  }
}
