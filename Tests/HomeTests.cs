using System.Collections;
using System.Reflection;

internal static class HomeTests
{
    public static void Run(Assembly a)
    {
        const BindingFlags p = BindingFlags.Instance | BindingFlags.NonPublic;
        using var form = (Form)Activator.CreateInstance(a.GetType("JoyIptv.PlayerForm")!, true)!;
        object Field(string name) => form.GetType().GetField(name, p)!.GetValue(form)!;
        var channels = (IList)Field("channels");
        var item = a.GetType("JoyIptv.Channel")!.GetConstructors()[0].Invoke(new object?[] {
            "Kaynak Filmi", "Sinema", "http://127.0.0.1/movie", "Film", null, 0, 0, "", "", "" });
        channels.Add(item);
        string key = (string)form.GetType().GetMethod("FavoriteKey", p)!.Invoke(form, new object[] { item })!;
        Field("favorites").GetType().GetMethod("Add")!.Invoke(Field("favorites"), new object[] { key });
        var build = form.GetType().GetMethod("BuildHomeSections", p);
        if (build == null) throw new Exception("FAIL: home is a flat catalog instead of current-source sections.");
        var sections = ((IEnumerable)build.Invoke(form, null)!).Cast<object>().ToList();
        var favorites = sections.FirstOrDefault(s => s.GetType().GetProperty("Title")!.GetValue(s)?.ToString() == "Favoriler");
        if (favorites == null || ((IEnumerable)favorites.GetType().GetProperty("Items")!.GetValue(favorites)!).Cast<object>().Count() != 1)
            throw new Exception("FAIL: home lost favorite from active source.");
        var address = (TextBox)Field("address"); address.Text = "http://127.0.0.1/other";
        sections = ((IEnumerable)build.Invoke(form, null)!).Cast<object>().ToList();
        if (sections.Any(s => s.GetType().GetProperty("Title")!.GetValue(s)?.ToString() == "Favoriler" &&
            ((IEnumerable)s.GetType().GetProperty("Items")!.GetValue(s)!).Cast<object>().Any()))
            throw new Exception("FAIL: home favorites crossed source boundary.");
        Console.WriteLine("PASS: home sections retain active-source favorites and isolate other sources.");
    }
}
