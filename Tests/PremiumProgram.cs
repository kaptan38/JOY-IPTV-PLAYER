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
            control.Dock = DockStyle.Fill;
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
                Check((int)top.GetValue(control)! <= index,
                    $"Selected row {index} scrolled outside viewport: top {top.GetValue(control)}, height {control.Height}.");
            }
            control.GetType().GetMethod("OnKeyDown", props)!.Invoke(control, new object[] { new KeyEventArgs(Keys.Home) });
            Check((int)selection.GetValue(control)! == 0, "Home did not select first channel.");
            control.GetType().GetMethod("OnKeyDown", props)!.Invoke(control, new object[] { new KeyEventArgs(Keys.End) });
            Check((int)selection.GetValue(control)! == 99999, "End did not select final channel.");
            foreach (var pair in new[] { (Keys.Up, 99998), (Keys.Down, 99999), (Keys.PageUp, 99999 - control.Height / 62), (Keys.Home, 0), (Keys.PageDown, control.Height / 62) })
            {
                control.GetType().GetMethod("OnKeyDown", props)!.Invoke(control, new object[] { new KeyEventArgs(pair.Item1) });
                Check((int)selection.GetValue(control)! == pair.Item2, "Keyboard navigation failed: " + pair.Item1);
            }
            int activated = 0;
            EventHandler activate = (_, _) => activated++;
            control.GetType().GetEvent("ItemActivated", props)!.AddEventHandler(control, activate);
            control.GetType().GetMethod("OnKeyDown", props)!.Invoke(control, new object[] { new KeyEventArgs(Keys.Enter) });
            Check(activated == 1, "Enter did not activate selected channel.");
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
            control.GetType().GetMethod("OnMouseDoubleClick", props)!.Invoke(control, new object[] { new MouseEventArgs(MouseButtons.Left, 2, 10, 10, 0) });
            Check(activated == 1, "Double-click on empty area activated a channel.");
            Console.WriteLine("PASS: 100,000 channels retain selection and draw only visible rows.");
            SearchTests.Run(assembly);
            int failures = 0;
            int caseIndex = Array.IndexOf(args, "--case");
            string selectedCases = caseIndex >= 0 && caseIndex + 1 < args.Length ? args[caseIndex + 1] : "all";
            foreach (var check in new (string Name, Action Run)[] {
                ("poster-cache", () => PosterCacheTests.Run(assembly)), ("recovery", () => FeatureTests.Recovery(assembly)),
                ("logs", () => LogTests.Run(assembly)), ("menu-layout", () => FeatureTests.Menu(assembly)),
                ("home", () => HomeTests.Run(assembly)), ("backup", () => FeatureTests.Backup(assembly)) })
            {
                if (selectedCases != "all" && !selectedCases.Split(',').Contains(check.Name)) continue;
                try { check.Run(); } catch (Exception ex) { failures++; Console.Error.WriteLine(check.Name + ": " + ex); }
            }
            return failures == 0 ? 0 : 1;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
