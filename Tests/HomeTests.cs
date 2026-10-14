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
        string sourceKey = (string)form.GetType().GetMethod("CacheKey", p)!.Invoke(form, null)!;
        var entryType = a.GetType("JoyIptv.ContinueEntry")!;
        var continues = (IDictionary)Field("continueItems");
        continues[key] = Activator.CreateInstance(entryType, new object[] { key, sourceKey, item, DateTime.UtcNow.AddHours(-1) })!;
        ((IDictionary)Field("resumePositions"))[key] = 120000L;
        continues["other"] = Activator.CreateInstance(entryType, new object[] { "other", "other-source", item, DateTime.UtcNow })!;
        ((IDictionary)Field("resumePositions"))["other"] = 60000L;
        ((IDictionary)Field("recentItems"))[key] = Activator.CreateInstance(entryType,
            new object[] { key, "other-source", item, DateTime.UtcNow.AddMinutes(1) })!;
        var build = form.GetType().GetMethods(p).SingleOrDefault(m => m.Name == "BuildHomeSections" && m.GetParameters().Length == 0);
        if (build == null) throw new Exception("FAIL: home is a flat catalog instead of current-source sections.");
        var sections = ((IEnumerable)build.Invoke(form, null)!).Cast<object>().ToList();
        var favorites = sections.FirstOrDefault(s => s.GetType().GetProperty("Title")!.GetValue(s)?.ToString() == "Favoriler");
        if (favorites == null || ((IEnumerable)favorites.GetType().GetProperty("Items")!.GetValue(favorites)!).Cast<object>().Count() != 1)
            throw new Exception("FAIL: home lost favorite from active source.");
        var resume = sections.Single(s => s.GetType().GetProperty("Title")!.GetValue(s)?.ToString() == "İzlemeye devam");
        var resumed = ((IEnumerable)resume.GetType().GetProperty("Items")!.GetValue(resume)!).Cast<object>().ToList();
        if (resumed.Count != 1 || (long)resumed[0].GetType().GetProperty("ResumeMs")!.GetValue(resumed[0])! != 120000)
            throw new Exception("FAIL: continue section lost position or crossed source.");
        var recent = sections.Single(s => s.GetType().GetProperty("Title")!.GetValue(s)?.ToString() == "Son izlenenler");
        if (((IEnumerable)recent.GetType().GetProperty("Items")!.GetValue(recent)!).Cast<object>().Count() != 1)
            throw new Exception("FAIL: identical channel identity in another source hides current-source history.");
        var directEpisode = a.GetType("JoyIptv.Channel")!.GetConstructors()[0].Invoke(new object?[] {
            "Dizi S01E01", "Diziler", "http://127.0.0.1/series/episode.mp4", "Dizi", null, 0, 0, "", "", "" });
        form.GetType().GetMethod("PrepareResume", p)!.Invoke(form, new[] { directEpisode });
        string episodeKey = (string)Field("playingKey");
        ((IDictionary)Field("resumePositions"))[episodeKey] = 90000L;
        form.GetType().GetMethod("PrepareResume", p)!.Invoke(form, new[] { directEpisode });
        if ((long)Field("pendingResume") != 90000L) throw new Exception("FAIL: direct M3U series episode cannot resume.");
        var address = (TextBox)Field("address"); address.Text = "http://127.0.0.1/other";
        sections = ((IEnumerable)build.Invoke(form, null)!).Cast<object>().ToList();
        if (sections.Any(s => s.GetType().GetProperty("Title")!.GetValue(s)?.ToString() == "Favoriler" &&
            ((IEnumerable)s.GetType().GetProperty("Items")!.GetValue(s)!).Cast<object>().Any()))
            throw new Exception("FAIL: home favorites crossed source boundary.");
        Console.WriteLine("PASS: home sections retain active-source favorites and isolate other sources.");
    }
}
