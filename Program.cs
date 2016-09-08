using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace IPCamSnapshotManager
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main()
        {
            if (Environment.UserInteractive)
            {
                IPCamSnapshotManager service = new IPCamSnapshotManager();
                service.TestStartupAndStop();
            }
            else
            {
                // Put the body of your old Main method here.
            }

            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new IPCamSnapshotManager()
            };
            ServiceBase.Run(ServicesToRun);
        }
    }
}
