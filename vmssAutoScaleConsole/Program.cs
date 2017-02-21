using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using vmssAutoScale.BL;
using vmssAutoScale.SqlLoadWatcher;

namespace vmssAutoScaleConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            while (!Console.KeyAvailable)
            {
                SqlLoadWatcher sqlLoadWatcher = new SqlLoadWatcher();
                AutoScaler autoScaler = new AutoScaler(sqlLoadWatcher);
                autoScaler.TraceEvent += AutoScaler_TraceEvent;
                Task t = autoScaler.AutoScale();

                t.Wait();
                Trace.WriteLine("Pausing for one minute");
                Task.Delay(60000).Wait();
            }
        }

        private static void AutoScaler_TraceEvent(object sender, string message)
        {
            Console.WriteLine(message);
        }
    }
}
