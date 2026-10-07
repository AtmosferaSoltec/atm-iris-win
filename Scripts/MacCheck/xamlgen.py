# Emits what the WinUI XAML compiler would generate for each page/control
# (x:Name fields, InitializeComponent, Bindings) so the app's C# compiles on a Mac.
import os, re, glob
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(HERE, "WinCheck", "obj", "Generated")
X = "http://schemas.microsoft.com/winfx/2006/xaml"
PRESENTATION = "http://schemas.microsoft.com/winfx/2006/xaml/presentation"

os.makedirs(OUT, exist_ok=True)
for f in glob.glob(os.path.join(OUT, "*.g.cs")):
    os.remove(f)

def type_name(tag):
    if not tag.startswith("{"):
        return tag
    uri, local = tag[1:].split("}")
    if uri == PRESENTATION:
        return local
    if uri.startswith("using:"):
        return "global::" + uri[len("using:"):] + "." + local
    return local

count = 0
for path in glob.glob(os.path.join(REPO, "**", "*.xaml"), recursive=True):
    rel = os.path.relpath(path, REPO)
    if rel.startswith(("obj", "bin", "Tests", "Scripts")):
        continue
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError as e:
        print(f"XAML-PARSE {rel}: {e}")
        continue
    cls = root.get(f"{{{X}}}Class")
    if not cls:
        continue
    ns, name = cls.rsplit(".", 1)
    fields = []
    for el in root.iter():
        n = el.get(f"{{{X}}}Name") or (el.get("Name") if el is not root else None)
        if n and el is not root and "." not in type_name(el.tag).split("::")[-1].split(".")[-1]:
            fields.append((type_name(el.tag), n))
    uses_bind = "x:Bind" in open(path, encoding="utf-8").read()
    lines = [
        "#nullable disable",
        "using Microsoft.UI.Xaml;",
        "using Microsoft.UI.Xaml.Controls;",
        "using Microsoft.UI.Xaml.Controls.Primitives;",
        "using Microsoft.UI.Xaml.Media;",
        "using Microsoft.UI.Xaml.Media.Animation;",
        "using Microsoft.UI.Xaml.Media.Imaging;",
        "using Microsoft.UI.Xaml.Shapes;",
        "using Microsoft.UI.Xaml.Documents;",
        f"namespace {ns}",
        "{",
        f"    partial class {name}",
        "    {",
    ]
    seen = set()
    for t, n in fields:
        if n in seen:
            continue
        seen.add(n)
        lines.append(f"        internal {t} {n};")
    lines.append("        public void InitializeComponent() { }")
    if uses_bind:
        lines.append("        private XamlBindings Bindings = new();")
        lines.append("        private sealed class XamlBindings { public void Update() { } public void Initialize() { } public void StopTracking() { } }")
    lines += ["    }", "}"]
    with open(os.path.join(OUT, f"{ns}.{name}.g.cs"), "w", encoding="utf-8") as out:
        out.write("\n".join(lines) + "\n")
    count += 1
