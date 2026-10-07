// Checks every {x:Bind} path of the Windows app's XAML against the compiled types, the way the
// WinUI XAML compiler would: each path segment must be a property, field or method of the type
// the previous segment produced. Also checks x:DataType types and {StaticResource} keys.
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

var repo = args[0];
var dll = args[1];
var refs = File.ReadAllLines(args[2]).Where(File.Exists).Append(dll).ToList();
var context = new MetadataLoadContext(new PathAssemblyResolver(refs), "System.Runtime");
var app = context.LoadFromAssemblyPath(dll);
var allTypes = new Dictionary<string, Type>();
foreach (var path in refs)
{
    try
    {
        foreach (var t in context.LoadFromAssemblyPath(path).GetTypes())
        {
            if (t.FullName is { } n)
            {
                allTypes.TryAdd(n, t);
            }
        }
    }
    catch
    {
    }
}

XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
string[] winuiNamespaces =
[
    "Microsoft.UI.Xaml.Controls", "Microsoft.UI.Xaml.Controls.Primitives", "Microsoft.UI.Xaml",
    "Microsoft.UI.Xaml.Media", "Microsoft.UI.Xaml.Media.Animation", "Microsoft.UI.Xaml.Shapes",
    "Microsoft.UI.Xaml.Documents", "Microsoft.UI.Xaml.Media.Imaging", "Microsoft.UI.Xaml.Input",
];
var problems = 0;
var checkedPaths = 0;

// Resource keys defined anywhere (app-wide dictionaries are merged into App.xaml).
var keys = new HashSet<string>();
var xamlFiles = Directory.GetFiles(repo, "*.xaml", SearchOption.AllDirectories)
    .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !f.Contains("/Tests/") && !f.Contains("/Scripts/"))
    .ToList();
foreach (var file in xamlFiles)
{
    foreach (var el in XDocument.Load(file).Descendants())
    {
        if (el.Attribute(x + "Key") is { } k)
        {
            keys.Add(k.Value);
        }
        else if (el.Name.LocalName == "Style" && el.Attribute("TargetType") is { } tt && el.Attribute(x + "Key") is null)
        {
            keys.Add(tt.Value);
        }
    }
}

// Built-in WinUI theme resources the app may use without defining them.
bool IsSystemKey(string key) => key.StartsWith("System") || key.StartsWith("Default") || key.StartsWith("Control") ||
    key.StartsWith("TextFill") || key.StartsWith("Accent") || key.StartsWith("Layer") || key.StartsWith("Card") ||
    key.StartsWith("Body") || key.StartsWith("Caption") || key.StartsWith("Subtle") || key.StartsWith("Solid") ||
    key.StartsWith("Text") || key.StartsWith("Button") || key.StartsWith("ListView") || key.StartsWith("Toggle") ||
    key.StartsWith("Flyout") || key.StartsWith("ComboBox") || key.StartsWith("Overlay") || key.StartsWith("Smoke") ||
    key.StartsWith("Acrylic") || key.StartsWith("Mica") || key.StartsWith("Scroll") || key.StartsWith("Menu");

foreach (var file in xamlFiles)
{
    var rel = Path.GetRelativePath(repo, file);
    var doc = XDocument.Load(file, LoadOptions.SetLineInfo);
    var root = doc.Root!;
    var prefixes = root.Attributes().Where(a => a.IsNamespaceDeclaration)
        .ToDictionary(a => a.Name.Namespace == XNamespace.None ? string.Empty : a.Name.LocalName, a => a.Value);
    Type? rootType = root.Attribute(x + "Class") is { } cls ? app.GetType(cls.Value) : null;
    if (root.Attribute(x + "Class") is { } c && rootType is null)
    {
        Report(root, $"x:Class {c.Value} not found");
    }

    foreach (var el in root.DescendantsAndSelf())
    {
        foreach (var attr in el.Attributes())
        {
            var value = attr.Value.Trim();
            foreach (Match m in Regex.Matches(value, @"\{StaticResource\s+([^}\s]+)\s*\}"))
            {
                var key = m.Groups[1].Value;
                if (!keys.Contains(key) && !IsSystemKey(key))
                {
                    Report(el, $"StaticResource '{key}' is not defined");
                }
            }

            if (!value.StartsWith("{x:Bind"))
            {
                continue;
            }

            var bindContext = ContextFor(el) ?? rootType;
            if (bindContext is null)
            {
                continue;
            }

            var inner = value[7..^1].Trim();
            var path = FirstArgument(inner);
            if (path.StartsWith("Path="))
            {
                path = path[5..];
            }

            if (path.Length == 0)
            {
                continue;
            }

            checkedPaths++;
            CheckPath(el, bindContext, path);
            foreach (var part in SplitTop(inner).Skip(1))
            {
                if (part.Trim().StartsWith("BindBack="))
                {
                    CheckPath(el, bindContext, part.Trim()[9..]);
                }
            }
        }
    }

    Type? ContextFor(XElement el)
    {
        for (var e = el; e is not null; e = e.Parent)
        {
            if (e != el || e.Name.LocalName == "DataTemplate")
            {
                if (e.Name.LocalName is "ControlTemplate")
                {
                    return null;
                }

                if (e.Attribute(x + "DataType") is { } dt && e.Name.LocalName == "DataTemplate")
                {
                    return ResolveTypeName(e, dt.Value);
                }
            }
        }

        return null;
    }

    Type? ResolveTypeName(XElement el, string name)
    {
        var parts = name.Split(':');
        var (prefix, local) = parts.Length == 2 ? (parts[0], parts[1]) : (string.Empty, parts[0]);
        if (prefixes.TryGetValue(prefix, out var ns) && ns.StartsWith("using:"))
        {
            var full = ns[6..] + "." + local;
            if (app.GetType(full) is { } t)
            {
                return t;
            }

            if (allTypes.TryGetValue(full, out var t2))
            {
                return t2;
            }

            Report(el, $"type '{name}' not found");
            return null;
        }

        foreach (var n in winuiNamespaces)
        {
            if (allTypes.TryGetValue(n + "." + local, out var t))
            {
                return t;
            }
        }

        if (allTypes.TryGetValue("System." + local, out var sys))
        {
            return sys;
        }

        return null;
    }

    void CheckPath(XElement el, Type start, string path)
    {
        path = path.Trim();
        if (path.StartsWith("'") || double.TryParse(path, System.Globalization.CultureInfo.InvariantCulture, out _) || path is "x:True" or "x:False" or "x:Null" or "true" or "false")
        {
            return;
        }

        Type? current = start;
        var segments = SplitSegments(path);
        var first = true;
        foreach (var raw in segments)
        {
            var seg = raw;
            if (current is null)
            {
                return;
            }

            // Static access through a namespace prefix: design:IrisTheme.HexBrush(...)
            if (first && seg.Contains(':'))
            {
                var typeName = seg;
                current = ResolveTypeName(el, typeName);
                first = false;
                if (current is null)
                {
                    return;
                }

                continue;
            }

            first = false;
            if (seg.StartsWith("("))
            {
                return; // casts and attached properties: not checked
            }

            var call = seg.IndexOf('(');
            string name = call >= 0 ? seg[..call] : seg;
            var bracket = name.IndexOf('[');
            if (bracket >= 0)
            {
                name = name[..bracket];
            }

            var member = Find(current, name);
            if (member is null)
            {
                Report(el, $"'{name}' is not a member of {current.FullName} (x:Bind {path})");
                return;
            }

            if (call >= 0)
            {
                var args = seg[(call + 1)..seg.LastIndexOf(')')];
                foreach (var arg in SplitTop(args))
                {
                    if (arg.Trim().Length > 0)
                    {
                        CheckPath(el, start, arg.Trim());
                    }
                }
            }

            current = member switch
            {
                PropertyInfo p => p.PropertyType,
                FieldInfo f => f.FieldType,
                MethodInfo mi => mi.ReturnType,
                EventInfo => null,
                _ => null,
            };
            if (bracket >= 0 && current is not null)
            {
                current = current.IsArray ? current.GetElementType() : current.GetGenericArguments().LastOrDefault();
            }
        }
    }

    void Report(XElement el, string message)
    {
        problems++;
        Console.WriteLine($"{rel}({((System.Xml.IXmlLineInfo)el).LineNumber}): {message}");
    }
}

Console.WriteLine(problems == 0 ? $"x:Bind check: OK ({checkedPaths} bindings, {keys.Count} resource keys)" : $"x:Bind check: {problems} problem(s) in {checkedPaths} bindings");
return problems == 0 ? 0 : 1;

static MemberInfo? Find(Type type, string name)
{
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
    for (var t = type; t is not null; t = t.BaseType)
    {
        try
        {
            var found = t.GetMember(name, flags).FirstOrDefault();
            if (found is not null)
            {
                return found;
            }
        }
        catch
        {
            return typeof(object); // unresolvable base: give up silently
        }
    }

    foreach (var i in SafeInterfaces(type))
    {
        var found = i.GetMember(name).FirstOrDefault();
        if (found is not null)
        {
            return found;
        }
    }

    return null;
}

static Type[] SafeInterfaces(Type t)
{
    try
    {
        return t.GetInterfaces();
    }
    catch
    {
        return [];
    }
}

static string FirstArgument(string inner) => SplitTop(inner).FirstOrDefault()?.Trim() is { } first && !first.Contains('=') || (SplitTop(inner).FirstOrDefault()?.Trim().StartsWith("Path=") ?? false)
    ? SplitTop(inner).First().Trim()
    : string.Empty;

static List<string> SplitTop(string text)
{
    var parts = new List<string>();
    var depth = 0;
    var inQuote = false;
    var start = 0;
    for (var i = 0; i < text.Length; i++)
    {
        var ch = text[i];
        if (ch == '\'')
        {
            inQuote = !inQuote;
        }
        else if (!inQuote && ch is '(' or '{' or '[')
        {
            depth++;
        }
        else if (!inQuote && ch is ')' or '}' or ']')
        {
            depth--;
        }
        else if (!inQuote && depth == 0 && ch == ',')
        {
            parts.Add(text[start..i]);
            start = i + 1;
        }
    }

    parts.Add(text[start..]);
    return parts;
}

static List<string> SplitSegments(string path)
{
    var parts = new List<string>();
    var depth = 0;
    var start = 0;
    for (var i = 0; i < path.Length; i++)
    {
        var ch = path[i];
        if (ch is '(' or '[')
        {
            depth++;
        }
        else if (ch is ')' or ']')
        {
            depth--;
        }
        else if (depth == 0 && ch == '.')
        {
            parts.Add(path[start..i]);
            start = i + 1;
        }
    }

    parts.Add(path[start..]);

    // "design:IrisTheme.HexBrush(x)" splits into ["design:IrisTheme", "HexBrush(x)"]: keep as is.
    return parts;
}
