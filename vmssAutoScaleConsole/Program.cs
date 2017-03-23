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
                double minutes = 0.1;
                Trace.WriteLine($"Pausing for {minutes} minutes");
                Task.Delay((int)(60*1000 * minutes)).Wait();
            }
        }

        private static void AutoScaler_TraceEvent(object sender, string message)
        {
            Console.WriteLine(message);
        }
    }
}
