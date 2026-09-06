using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace JosephExperience.Utilities;

public static class ObjModelLoader
{
    private static readonly ConcurrentDictionary<string, Model3DGroup> _cache = new();

    public static Model3DGroup Load(string objPath)
    {
        if (!File.Exists(objPath)) return null;

        var fullPath = Path.GetFullPath(objPath);
        var writeTime = File.GetLastWriteTimeUtc(fullPath);
        var key = $"{fullPath}\0{writeTime.Ticks}";

        if (_cache.TryGetValue(key, out var cached)) return cached;

        var group = Parse(objPath, fullPath);
        if (group != null) _cache[key] = group;
        return group;
    }

    private static Model3DGroup Parse(string objPath, string fullPath)
    {
        var dir = Path.GetDirectoryName(fullPath) ?? "";
        var lines = File.ReadAllLines(fullPath);
        var positions = new List<Point3D>();
        var normals = new List<Vector3D>();
        var texCoords = new List<Point>();
        var materials = new Dictionary<string, (Brush brush, Color color)>(StringComparer.OrdinalIgnoreCase);
        string currentMaterial = null;
        var builders = new Dictionary<string, _MeshBuilder>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
            var sp = line.IndexOf(' ');
            if (sp < 0) continue;
            var tag = line.Substring(0, sp);
            var args = line.Substring(sp + 1).Trim();
            switch (tag)
            {
                case "v": ParseV(args, positions); break;
                case "vn": ParseVN(args, normals); break;
                case "vt": ParseVT(args, texCoords); break;
                case "mtllib":
                    foreach (var m in args.Split(null as string[], StringSplitOptions.RemoveEmptyEntries))
                        LoadMtl(Path.Combine(dir, m), materials, dir);
                    break;
                case "usemtl":
                    currentMaterial = args;
                    if (!builders.ContainsKey(currentMaterial)) { builders[currentMaterial] = new _MeshBuilder(); order.Add(currentMaterial); }
                    break;
                case "f":
                    if (currentMaterial != null && builders.TryGetValue(currentMaterial, out var fb))
                        FaceData(fb, args, positions, normals, texCoords);
                    break;
            }
        }

        if (positions.Count == 0) return null;
        var group = new Model3DGroup();
        foreach (var name in order)
        {
            var mb = builders[name];
            if (mb.Positions.Count == 0) continue;
            var geom = new MeshGeometry3D {
                Positions = new Point3DCollection(mb.Positions),
                TriangleIndices = new Int32Collection(mb.Indices),
                TextureCoordinates = mb.TexCoords.Count > 0 ? new PointCollection(mb.TexCoords) : null,
                Normals = mb.Normals.Count > 0 ? new Vector3DCollection(mb.Normals) : null };
            var col = materials.TryGetValue(name, out var mt) ? mt.color : Colors.LightGray;
            var brush = materials.TryGetValue(name, out var mt2) ? mt2.brush : null;
            Material mat = brush != null ? new DiffuseMaterial(brush) : new DiffuseMaterial(new SolidColorBrush(col));
            group.Children.Add(new GeometryModel3D(geom, mat) { BackMaterial = mat });
        }
        group.Freeze();
        return group;
    }

    private static void LoadMtl(string path, Dictionary<string, (Brush brush, Color color)> materials, string dir)
    {
        if (!File.Exists(path)) return;
        string cur = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
            var sp = line.IndexOf(' ');
            if (sp < 0) continue;
            var tag = line.Substring(0, sp);
            var args = line.Substring(sp + 1).Trim();
            switch (tag)
            {
                case "newmtl":
                    cur = args;
                    if (!materials.ContainsKey(cur)) materials[cur] = (null, Colors.LightGray);
                    break;
                case "Kd":
                    if (cur != null) ParseColor(args, materials, cur);
                    break;
                case "map_Kd":
                    if (cur != null)
                    {
                        var tp = Path.Combine(dir, args.Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(tp)) tp = Path.Combine(dir, Path.GetFileName(args));
                        Brush br = null;
                        if (File.Exists(tp))
                        {
                            try
                            {
                                var bmp = new BitmapImage();
                                bmp.BeginInit();
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.UriSource = new Uri(tp);
                                bmp.EndInit(); bmp.Freeze();
                                br = new ImageBrush(bmp) { ViewportUnits = BrushMappingMode.Absolute }; br.Freeze();
                            } catch { }
                        }
                        if (br != null) { var e = materials[cur]; materials[cur] = (br, e.color); }
                    }
                    break;
            }
        }
    }

    private static void ParseColor(string args, Dictionary<string, (Brush brush, Color color)> materials, string cur)
    {
        var p = args.Split(null as string[], StringSplitOptions.RemoveEmptyEntries);
        if (p.Length >= 3 &&
            double.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) &&
            double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var g) &&
            double.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b))
        {
            var e = materials[cur]; materials[cur] = (e.brush, Color.FromScRgb(1f, (float)r, (float)g, (float)b));
        }
    }

    private static void ParseV(string s, List<Point3D> l)
    {
        var p = s.Split(null as string[], StringSplitOptions.RemoveEmptyEntries);
        if (p.Length >= 3 &&
            double.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) &&
            double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) &&
            double.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z))
            l.Add(new Point3D(x, y, z));
    }

    private static void ParseVN(string s, List<Vector3D> l)
    {
        var p = s.Split(null as string[], StringSplitOptions.RemoveEmptyEntries);
        if (p.Length >= 3 &&
            double.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) &&
            double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) &&
            double.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z))
        {
            var v = new Vector3D(x, y, z);
            l.Add(v.LengthSquared < 1e-12 ? v : Vector3D.Multiply(1.0 / v.Length, v));
        }
    }

    private static void ParseVT(string s, List<Point> l)
    {
        var p = s.Split(null as string[], StringSplitOptions.RemoveEmptyEntries);
        if (p.Length >= 2 &&
            double.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var u) &&
            double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w))
            l.Add(new Point(u, 1.0 - w));
    }

    private static void FaceData(_MeshBuilder mb, string args, List<Point3D> pos, List<Vector3D> norms, List<Point> uvs)
    {
        var verts = args.Split(null as string[], StringSplitOptions.RemoveEmptyEntries);
        var idx = new List<int>();
        foreach (var v in verts)
        {
            var parts = v.Split('/');
            int vi = ParseI(parts[0]);
            int ti = parts.Length > 1 && parts[1].Length > 0 ? ParseI(parts[1]) : 0;
            int ni = parts.Length > 2 ? ParseI(parts[2]) : 0;
            int pIdx = vi > 0 ? vi - 1 : pos.Count + vi;
            int tIdx = ti > 0 ? ti - 1 : (ti < 0 ? uvs.Count + ti : 0);
            int nIdx = ni > 0 ? ni - 1 : (ni < 0 ? norms.Count + ni : 0);
            if (pIdx < 0 || pIdx >= pos.Count) continue;
            var position = pos[pIdx];
            Point uv = (tIdx >= 0 && tIdx < uvs.Count) ? uvs[tIdx] : new Point(0.5, 0.5);
            Vector3D? normal = (nIdx >= 0 && nIdx < norms.Count) ? norms[nIdx] : null;
            mb.Positions.Add(position);
            mb.TexCoords.Add(uv);
            if (normal.HasValue) mb.Normals.Add(normal.Value);
            idx.Add(mb.Positions.Count - 1);
        }
        for (int i = 1; i + 1 < idx.Count; i++) { mb.Indices.Add(idx[0]); mb.Indices.Add(idx[i]); mb.Indices.Add(idx[i + 1]); }
    }

    private static int ParseI(string s) => int.TryParse(s, out var r) ? r : 0;

    private sealed class _MeshBuilder
    {
        public List<Point3D> Positions = new();
        public List<Point> TexCoords = new();
        public List<Vector3D> Normals = new();
        public List<int> Indices = new();
    }
}
