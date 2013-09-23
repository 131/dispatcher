using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using Utils;

namespace echo
{
  class Program
  {
    static void Main()
    {

      Stream instrm = Console.OpenStandardInput();
      Stream outstrm = Console.OpenStandardOutput();


      byte[] rbuffer = new byte[1024];
      StreamState state = new StreamState { Buffer = rbuffer, rStream = instrm, wStream = outstrm };
      instrm.BeginRead(rbuffer, 0, rbuffer.Length, new AsyncCallback(sRead), state);

      while (!state.EOF) ;

    }

    static void sRead(IAsyncResult async)
    {
      StreamState state = (StreamState)async.AsyncState;
      int dataLen = state.rStream.EndRead(async);
      if (dataLen == 0)
        state.EOF = true;

      string str = UTF8Encoding.UTF8.GetString(state.Buffer, 0, dataLen);
      state.wStream.Write(state.Buffer, 0, dataLen);

      //state.wStream.Close();
      //proc.StandardInput.BaseStream.Flush();
      state.rStream.BeginRead(state.Buffer, 0, state.Buffer.Length, new AsyncCallback(sRead), state);
    }

  }





}
