using System;
using System.Collections.Generic;
using System.Linq;
using ZwaveMqttTemplater.Z2M;

namespace ZwaveMqttTemplater
{
    class Z2MContainer
    {
        private readonly List<Z2MNode> _nodes;

        public Z2MContainer(List<Z2MNode> nodes)
        {
            _nodes = nodes;
        }

        private IEnumerable<Z2MNode> GetBaseQuery()
        {
            return _nodes.Where(s => !s.failed);
        }

        public IEnumerable<Z2MNode> GetByName(string filter)
        {
            // TODO: wildcards
            return GetBaseQuery().Where(s => s.name?.Contains(filter) ?? false);
        }

        public IEnumerable<Z2MNode> GetByProduct(string filter)
        {
            // TODO: wildcards
            return GetBaseQuery().Where(s => s.product.Contains(filter));
        }

        public IEnumerable<Z2MNode> GetById(string filter)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Z2MNode> GetByManufacturer(string filter)
        {
            throw new NotImplementedException();
        }

        public Z2MValue GetCurrentValue(int nodeId, int commandClass, int instance, int index)
        {
            var node = _nodes[nodeId];

            if (!node.values.TryGetValue($"{commandClass}-{instance}-{index}", out Z2MValue valueSpec))
                throw new Exception();

            return valueSpec;
        }

        public IEnumerable<Z2MNode> GetAll(bool includeRemoved = false)
        {
            return _nodes.Where(s => includeRemoved || !s.failed);
        }

        public Z2MNode GetNode(int nodeId)
        {
            return _nodes[nodeId];
        }
    }
}