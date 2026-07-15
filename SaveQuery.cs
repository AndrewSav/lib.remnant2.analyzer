using System.Text.RegularExpressions;
using lib.remnant2.saves.Model;
using lib.remnant2.saves.Model.Properties;

namespace lib.remnant2.analyzer;

// This is a trimmed down version of lib.remnant2.saves Navigator tuned for performance in this library
internal sealed class SaveQuery(SaveFile saveFile)
{
    private static readonly HashSet<string> Indexed =
        ["Property", "Variable", "Actor", "UObject", "Component", "ArrayStructProperty"];

    private readonly SaveData _saveData = saveFile.SaveData;
    private Dictionary<(string Type, string Name), List<ModelBase>>? _index;
    private Dictionary<ModelBase, ModelBase?>? _parent;

    public UObject Root => _saveData.Objects[0];

    public Property? RootProperty(string name) =>
        Root.Properties != null && Root.Properties.Lookup.TryGetValue(name, out Property? p) ? p : null;

    public Property? GetProperty(string name, ModelBase? parent = null) => Single<Property>(name, parent);
    public List<Property> GetProperties(string name, ModelBase? parent = null) => All<Property>(name, parent);
    public Component? GetComponent(string name, ModelBase? parent = null) => Single<Component>(name, parent);
    public List<Component> GetComponents(string name, ModelBase? parent = null) => All<Component>(name, parent);
    public Actor? GetActor(string name, ModelBase? parent = null) => Single<Actor>(name, parent);
    public List<Actor> GetActors(string name, ModelBase? parent = null) => All<Actor>(name, parent);
    public UObject? GetObject(string name, ModelBase? parent = null) => Single<UObject>(name, parent);
    public List<UObject> GetObjects(string name, ModelBase? parent = null) => All<UObject>(name, parent);
    public List<UObject> FindObjects(string namePattern, ModelBase? parent = null) => Find<UObject>(namePattern, parent);

    private List<T> All<T>(string name, ModelBase? parent) where T : ModelBase
    {
        EnsureIndex();
        if (!_index!.TryGetValue((typeof(T).Name, name), out List<ModelBase>? matches)) return [];
        if (parent == null) return matches.ConvertAll(x => (T)x);
        List<T> result = [];
        foreach (ModelBase m in matches)
            if (IsDescendantOrSelf(m, parent)) result.Add((T)m);
        return result;
    }

    private T? Single<T>(string name, ModelBase? parent) where T : ModelBase
    {
        List<T> l = All<T>(name, parent);
        return l.Count switch
        {
            0 => null,
            1 => l[0],
            _ => throw new InvalidOperationException("there are more than one item")
        };
    }

    // Regex name match: scan the index's entries of type T whose name matches the
    // pattern, in index (BFS first-occurrence) order, optionally scoped to a parent.
    private List<T> Find<T>(string namePattern, ModelBase? parent) where T : ModelBase
    {
        EnsureIndex();
        string type = typeof(T).Name;
        List<T> result = [];
        foreach (KeyValuePair<(string Type, string Name), List<ModelBase>> kv in _index!)
        {
            if (kv.Key.Type != type || !Regex.IsMatch(kv.Key.Name, namePattern)) continue;
            foreach (ModelBase m in kv.Value)
                if (parent == null || IsDescendantOrSelf(m, parent)) result.Add((T)m);
        }
        return result;
    }

    private bool IsDescendantOrSelf(ModelBase node, ModelBase ancestor)
    {
        ModelBase? current = node;
        while (current != null)
        {
            if (ReferenceEquals(current, ancestor)) return true;
            current = _parent!.GetValueOrDefault(current);
        }
        return false;
    }

    private void EnsureIndex()
    {
        if (_index != null) return;
        _index = [];
        _parent = [];
        Queue<ModelBase> q = new();
        q.Enqueue(_saveData);
        _parent[_saveData] = null;
        while (q.Count > 0)
        {
            ModelBase n = q.Dequeue();
            string type = n.GetType().Name;
            if (Indexed.Contains(type))
            {
                string name = GetName(n);
                if (name.Length > 0)
                {
                    (string, string) key = (type, name);
                    if (!_index.TryGetValue(key, out List<ModelBase>? list)) _index[key] = list = [];
                    list.Add(n);
                }
            }
            foreach ((ModelBase child, int? _) in n.GetChildren())
            {
                _parent[child] = n;
                q.Enqueue(child);
            }
        }
    }

    private static string GetName(ModelBase item) => item switch
    {
        Variable x => x.Name.Name,
        UObject x => GetUObjectName(x),
        Component x => x.ComponentKey,
        Property x => x.Name.Name,
        Actor x => x.DynamicData?.ClassPath.Name,
        ArrayStructProperty x => x.ElementType.Name,
        _ => ""
    } ?? "";

    private static string? GetUObjectName(UObject o)
    {
        if (o.Name == "PersistenceContainer") return $"pc:{o.KeySelector}";
        if (o.Name == "ZoneActor" && (o.Properties?.Contains("Label") ?? false))
            return "za:" + o.Properties["Label"].Get<TextProperty>();
        return o.Name;
    }
}
