using System.Collections;
using System.Reflection;

internal static class SearchTests
{
    const BindingFlags P = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Field(Form form, string name) => form.GetType().GetField(name, P)!.GetValue(form)!;
    static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); }
    static void Wait(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!done() && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(2); }
        Check(done(), "Search did not settle.");
    }
    public static void Run(Assembly assembly)
    {
        using var form = (Form)Activator.CreateInstance(assembly.GetType("JoyIptv.PlayerForm")!, true)!;
        var kind = (ComboBox)Field(form, "kinds"); kind.SelectedItem = "Canlı TV";
        var catalog = (IList)Field(form, "channels");
        var ctor = assembly.GetType("JoyIptv.Channel")!.GetConstructors()[0];
        for (int i = 0; i < 100000; i++) catalog.Add(ctor.Invoke(new object?[] {
            "Türkçe Kanal " + i, "Ulusal", "http://127.0.0.1/" + i, "Canlı TV", null, 0, i, "", "", "" }));
        var filter = (TextBox)Field(form, "filter");
        int ticks = 0;
        using var timer = new System.Windows.Forms.Timer { Interval = 10 };
        timer.Tick += (_, _) => ticks++; timer.Start();
        filter.Text = "Türkçe Kanal 99999";
        Wait(() => ((IList)Field(form, "visible")).Count == 1);
        timer.Stop();
        Check(ticks > 0, "100,000-channel search blocks the UI heartbeat.");
        filter.Text = "Türkçe Kanal"; filter.Text = "Türkçe Kanal 17"; filter.Text = "Türkçe Kanal 99999";
        Wait(() => ((IList)Field(form, "visible")).Count == 1);
        var result = (IList)Field(form, "visible");
        Check(result[0]!.GetType().GetProperty("Name")!.GetValue(result[0])?.ToString() == "Türkçe Kanal 99999", "Old query won.");
        filter.Text = "Türkçe Kanal";
        catalog.Clear(); catalog.Add(ctor.Invoke(new object?[] { "Yeni Kaynak", "Yeni", "http://127.0.0.1/new", "Canlı TV", null, 0, 0, "", "", "" }));
        filter.Clear(); form.GetType().GetMethod("ApplyFilter", P)!.Invoke(form, null);
        Wait(() => ((IList)Field(form, "visible")).Count == 1 &&
            ((IList)Field(form, "visible"))[0]!.GetType().GetProperty("Name")!.GetValue(((IList)Field(form, "visible"))[0])?.ToString() == "Yeni Kaynak");
        var deadline = DateTime.UtcNow.AddMilliseconds(300);
        while (DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(2); }
        Check(((IList)Field(form, "visible")).Count == 1, "Previous source search replaced new catalog.");
        Console.WriteLine("PASS: background search stays responsive; latest query/source wins.");
    }
}
