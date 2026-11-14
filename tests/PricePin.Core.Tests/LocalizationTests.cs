using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PricePin.Core.Tests;

/// <summary>
/// Checks the UI string files without starting WPF: every translation has the same keys and
/// placeholders as English, and every key the app uses exists.
/// </summary>
public class LocalizationTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string AppDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PricePin.sln")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "src", "PricePin");
        }
    }

    private static Dictionary<string, string> Load(string code) =>
        XDocument.Load(Path.Combine(AppDir, "Strings", $"Strings.{code}.xaml"))
            .Root!.Elements()
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, e => e.Value);

    private static IEnumerable<string> Translations() =>
        Directory.GetFiles(Path.Combine(AppDir, "Strings"), "Strings.*.xaml")
            .Select(f => Path.GetFileNameWithoutExtension(f)["Strings.".Length..])
            .Where(c => c != "en");

    private static SortedSet<string> Placeholders(string text) =>
        new(Regex.Matches(text, @"\{(\d+)(?:[:,][^}]*)?\}").Select(m => m.Groups[1].Value));

    [Fact]
    public void There_is_at_least_one_translation() => Assert.Contains("tr", Translations());

    [Fact]
    public void Translations_have_the_same_keys_and_placeholders_as_English()
    {
        var en = Load("en");
        foreach (var code in Translations())
        {
            var other = Load(code);
            Assert.Empty(en.Keys.Except(other.Keys));      // missing translations
            Assert.Empty(other.Keys.Except(en.Keys));      // stale keys
            foreach (var (key, text) in en)
                Assert.True(Placeholders(text).SetEquals(Placeholders(other[key])), $"{code}: {key} placeholders differ");
        }
    }

    [Fact]
    public void Every_key_used_by_the_app_exists()
    {
        var en = Load("en");
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(AppDir, "*.xaml", SearchOption.AllDirectories))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"DynamicResource ([A-Z][A-Za-z]+\.[A-Za-z.]+)\}"))
                used.Add(m.Groups[1].Value);
        foreach (var file in Directory.EnumerateFiles(AppDir, "*.cs", SearchOption.AllDirectories))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"Loc\.[TF]\(""([^""]+)"""))
                used.Add(m.Groups[1].Value);

        Assert.NotEmpty(used);
        Assert.DoesNotContain(used, k => !en.ContainsKey(k));
    }
}
