using System.Windows;
using System.Diagnostics;

namespace Absolute.App;

public partial class App : Application
{
    internal static readonly Stopwatch StartupClock = new();
    public App() => StartupClock.Start();
}
