using System.Collections;
using System.Reflection;
using LibVLCSharp.Shared;

internal static class PremiumProgram
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Core.Initialize(args[0]); Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            var assembly = Assembly.Load("JOY_IPTV_Player");
            using var form = (Form)Activator.CreateInstance(assembly.GetType("JoyIptv.PlayerForm")!, true)!;
            var control = (Control)form.GetType().GetField("list", Private)!.GetValue(form)!;
            // A native ListBox eagerly holds formatted strings for all entries. A virtual control must not.
            Check(control is ScrollableControl && control is not ListBox,
                "Large channel catalog still binds every row into a native ListBox.");
            Console.WriteLine("PASS: live list uses visible-only scrollable rendering.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
