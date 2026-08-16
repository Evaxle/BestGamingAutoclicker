using Avalonia;
using System;

namespace BestAutoClicker;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => App.BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);
}
