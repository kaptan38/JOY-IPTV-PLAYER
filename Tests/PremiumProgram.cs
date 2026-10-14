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
            // Exercise the control in isolation: the form's initial background
            // catalog query is allowed to replace its own binding during message pumping.
            using var virtualControl = (Control)Activator.CreateInstance(control.GetType(), true)!;
            control = virtualControl;
            var channelType = assembly.GetType("JoyIptv.Channel")!;
            var entries = Array.CreateInstance(channelType, 100000);
            var ctor = channelType.GetConstructors()[0];
            for (int i = 0; i < entries.Length; i++) entries.SetValue(ctor.Invoke(new object?[] {
                "Kanal " + i, "Ulusal", "http://127.0.0.1/" + i, "Canlı TV", null, 0, i, "", "", "" }), i);
            var props = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            control.GetType().GetMethod("SetItems", props)!.Invoke(control, new object[] { entries, 0 });
            using var host = new Form { Width = 320, Height = 460 };
            control.Parent?.Controls.Remove(control); host.Controls.Add(control); host.Show(); Application.DoEvents();
            var selection = control.GetType().GetProperty("SelectedIndex", props)!;
            var top = control.GetType().GetProperty("TopIndex", props)!;
            foreach (int index in new[] { 0, 50000, 99999 })
            {
                selection.SetValue(control, index); Application.DoEvents();
                Check((int)selection.GetValue(control)! == index, "Virtual selection lost at " + index);
                Check((int)top.GetValue(control)! <= index, "Selected row scrolled outside viewport.");
            }
            control.GetType().GetMethod("OnKeyDown", props)!.Invoke(control, new object[] { new KeyEventArgs(Keys.Home) });
            Check((int)selection.GetValue(control)! == 0, "Home did not select first channel.");
            control.GetType().GetMethod("OnKeyDown", props)!.Invoke(control, new object[] { new KeyEventArgs(Keys.End) });
            Check((int)selection.GetValue(control)! == 99999, "End did not select final channel.");
            int drawn = 0;
            DrawItemEventHandler handler = (_, _) => drawn++;
            control.GetType().GetEvent("DrawItem", props)!.AddEventHandler(control, handler);
            using var image = new Bitmap(control.Width, control.Height);
            control.DrawToBitmap(image, new Rectangle(Point.Empty, control.Size));
            int rowHeight = (int)control.GetType().GetProperty("ItemHeight", props)!.GetValue(control)!;
            Check(drawn > 0 && drawn <= control.Height / rowHeight + 2, "Rendered off-screen channel rows.");
            control.GetType().GetEvent("DrawItem", props)!.RemoveEventHandler(control, handler);
            control.GetType().GetMethod("SetItems", props)!.Invoke(control, new object[] { Array.CreateInstance(channelType, 0), -1 });
            Check((int)control.GetType().GetMethod("IndexFromPoint", props)!.Invoke(control, new object[] { new Point(10, 10) })! == -1,
                "Empty area identified as a channel.");
            Console.WriteLine("PASS: 100,000 channels retain selection and draw only visible rows.");
            SearchTests.Run(assembly);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
