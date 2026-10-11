// (c) J@mBeL.net 2010-2026. This file is part of jaNET Framework, GNU GPL version 3 or later.
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

switch (args[0]) {
    case "echo": Console.Write(args[1]); break;
    case "both":
        await Task.WhenAll(Console.Out.WriteAsync(new string('o', 131072)), Console.Error.WriteAsync(new string('e', 131072)));
        break;
    case "overflow": Console.Write(new string('x', 1_048_577)); break;
    case "fail": Console.Error.Write("probe failed"); return 7;
    case "sleep":
        File.WriteAllText(args[1], Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        await Task.Delay(60000); break;
    case "child":
    case "pipe":
        var start = new ProcessStartInfo(Environment.ProcessPath!) {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("sleep"); start.ArgumentList.Add(args[1]);
        using (Process child = Process.Start(start)!) {
            File.WriteAllText(args[2], Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            if (args[0] == "child") await Task.Delay(60000);
            else {
                // Ensure the child has inherited stdout before the parent exits.
                for (int i = 0; i < 100 && !File.Exists(args[1]); i++) await Task.Delay(20);
            }
        }
        break;
}
return 0;
