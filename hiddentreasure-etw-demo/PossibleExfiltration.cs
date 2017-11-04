﻿// Copyright (c) Zac Brown. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using O365.Security.ETW;

namespace hiddentreasure_etw_demo
{
    public static class PossibleExfiltration
    {
        static Dictionary<int, Dictionary<IPAddress, uint>> pidToDestination = new Dictionary<int, Dictionary<IPAddress, uint>>();

        private static Provider CreateNetworkProvider()
        {
            var filter = new EventFilter(
                Filter.EventIdIs(10) // IPv4 send
                .Or(Filter.EventIdIs(26))); // IPv6 send

            filter.OnEvent += (IEventRecord r) => {
                var daddr = r.GetIPAddress("daddr"); // destination
                var bytes = r.GetUInt32("size"); // size in bytes
                var pid = (int)r.ProcessId;

                // if we don't have the PID in our table, add it
                if (!pidToDestination.ContainsKey(pid)) pidToDestination[pid] = new Dictionary<IPAddress, uint>();

                // if we've never seen the destination, set it to zero
                if (!pidToDestination[pid].ContainsKey(daddr)) pidToDestination[pid][daddr] = 0;

                pidToDestination[pid][daddr] += bytes;
            };

            var provider = new Provider("Microsoft-Windows-Kernel-Network");
            provider.AddFilter(filter);

            return provider;
        }

        private static Provider CreateProcessProvider()
        {
            var filter = new EventFilter(
                Filter.EventIdIs(2)); // process end

            filter.OnEvent += (IEventRecord r) => {
                var pid = (int)r.ProcessId;

                // if we've seen any network traffic
                if (pidToDestination.ContainsKey(pid))
                {
                    var destinationData = pidToDestination[pid];
                    pidToDestination.Remove(pid);

                    string processName = r.GetAnsiString("ImageName");

                    foreach (var destination in destinationData)
                    {
                        if (destination.Value < (1024 * 1024)) return; // 1MB threshold

                        Console.WriteLine($"{processName} (pid: {pid}) transferred "
                            + $"{destination.Value} bytes"
                            + $" to {destination.Key.ToString()}");
                    }
                }
            };

            var provider = new Provider("Microsoft-Windows-Kernel-Process");
            provider.AddFilter(filter);

            return provider;
        }

        public static void Run()
        {
            var networkProvider = CreateNetworkProvider();
            var processProvider = CreateProcessProvider();

            var trace = new UserTrace();
            trace.Enable(networkProvider);
            trace.Enable(processProvider);

            // Setup Ctrl-C to call trace.Stop();
            Helpers.SetupCtrlC(trace);

            // This call is blocking. The thread that calls UserTrace.Start()
            // is donating itself to the ETW subsystem to pump events off
            // of the buffer.
            trace.Start();
        }
    }
}
