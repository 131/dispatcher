using System;
using System.Management;

namespace Dispatcher
{

    public class UWFManagement
    {

        // return Whether or not servicing mode is currently enabled

        public static bool servicingEnabled()
        {
            var scope = new ManagementScope(@"root\standardcimv2\embedded");
            var uwfClass = new ManagementClass(scope.Path.Path, "UWF_Servicing", null);

            try
            {
                foreach (ManagementObject instance in uwfClass.GetInstances())
                {
                    var currentSession = Convert.ToBoolean(instance.GetPropertyValue("CurrentSession").ToString());
                    if (!currentSession)
                        continue;

                    return Convert.ToBoolean(instance.GetPropertyValue("ServicingEnabled").ToString());
                }
            }
            catch (Exception)
            {

            }

            return false;
        }

    }
}


