using System;
using System.Diagnostics;
using System.IO;

if (args.Length == 0)
{
    Console.WriteLine("Usage: HangmanGameLauncher.exe <path-to-index-html>");
    return 1;
}

var indexPath = Path.GetFullPath(args[0]);

if (!File.Exists(indexPath))
{
    Console.WriteLine($"File not found: {indexPath}");
    return 1;
}

var startInfo = new ProcessStartInfo(indexPath)
{
    UseShellExecute = true
};

Process.Start(startInfo);
Console.WriteLine($"Opening game in browser: {indexPath}");
return 0;
