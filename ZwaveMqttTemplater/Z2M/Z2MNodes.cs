using System.Text.RegularExpressions;
using ZwaveMqttTemplater.Z2M.Models;

namespace ZwaveMqttTemplater.Z2M;

internal class Z2MNodes
{
    private static readonly Regex FilterRegex = new(@"\b(?<type>\w+):(?<value>[\w\s\d\-]+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Dictionary<int, Z2MNode> _nodes;

    public Z2MNodes(IEnumerable<Z2MNode> nodes)
    {
        _nodes = nodes.ToDictionary(s => s.id);
    }

    private IEnumerable<Z2MNode> GetBaseQuery()
    {
        return _nodes.Values.Where(s => !s.failed);
    }

    public IEnumerable<Z2MNode> GetByNameFilter(string filter)
    {
        // TODO: wildcards
        return GetBaseQuery().Where(s => s.name?.Contains(filter) ?? false);
    }

    public IEnumerable<Z2MNode> GetByName(string name)
    {
        return GetBaseQuery().Where(s => s.name?.Equals(name, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    public IEnumerable<Z2MNode> GetByProduct(string filter)
    {
        // TODO: wildcards
        return GetBaseQuery().Where(s => s.productLabel != null && s.productLabel.Contains(filter));
    }

    public Z2MNode GetById(string filter)
    {
        int id = int.Parse(filter);
        return GetById(id);
    }

    public Z2MNode GetById(int id)
    {
        return _nodes[id];
    }

    public IEnumerable<Z2MNode> GetByManufacturer(string filter)
    {
        throw new NotImplementedException();
    }

    public Z2MValue GetValue(int nodeId, string key)
    {
        Z2MNode node = _nodes[nodeId];

        if (!node.values.TryGetValue(key, out Z2MValue valueSpec))
            throw new Exception();

        return valueSpec;
    }

    public IEnumerable<Z2MNode> GetAll(bool includeRemoved = false)
    {
        return _nodes.Values.Where(s => includeRemoved || !s.failed);
    }

    public Z2MNode GetNode(int nodeId)
    {
        return _nodes[nodeId];
    }

    public IEnumerable<Z2MNode> FilterByString(string query)
    {
        IEnumerable<Z2MNode> selection = _nodes.Values;
        if (string.IsNullOrEmpty(query))
            return selection;

        MatchCollection matches = FilterRegex.Matches(query);

        foreach (Match match in matches)
        {
            string type = match.Groups["type"].Value;
            string value = match.Groups["value"].Value;

            switch (type)
            {
                case "name":
                    selection = selection.Where(s => s.name?.Contains(value) ?? false);
                    break;
                case "product":
                    selection = selection.Where(s => s.productLabel?.Contains(value) ?? false);
                    break;
                case "manufacturer":
                    selection = selection.Where(s => s.manufacturer?.Contains(value) ?? false);
                    break;
                case "id":
                    int idInt = int.Parse(value);
                    selection = selection.Where(s => s.id == idInt);
                    break;
                case "flag":
                    if (value == "awake")
                        selection = selection.Where(s => s.status == "Alive");
                    else if (value == "asleep")
                        selection = selection.Where(s => s.status == "Asleep");
                    break;
                default:
                    throw new Exception();
            }
        }

        return selection;
    }
}