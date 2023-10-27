using System;
using System.Management;
using System.Threading;

namespace Dispatcher
{

    public class UWFManagement
    {

       // return Whether or not servicing mode is currently enabled

       public static bool servicingEnabled()
        {
            var scope = new ManagementScope(@"root\standardcimv2\embedded");
            var uwfClass = new ManagementClass(scope.Path.Path, "UWF_Servicing", null);

            bool servicingEnabled = false;

            AutoResetEvent operationComplete = new AutoResetEvent(false);

            Thread watchdogThread = new Thread(() => {
                if (!operationComplete.WaitOne(10000))
                    Environment.Exit(1);
            });

            try  {
              watchdogThread.Start();

              foreach (ManagementObject instance in uwfClass.GetInstances()) {
                  var currentSession = Convert.ToBoolean(instance.GetPropertyValue("CurrentSession").ToString());
                  if (!currentSession)
                      continue;

                  servicingEnabled = Convert.ToBoolean(instance.GetPropertyValue("ServicingEnabled").ToString());
              }
            } catch (Exception) { }
            finally {
              operationComplete.Set();
              watchdogThread.Join();
            }

            return servicingEnabled;
        }
    }
}