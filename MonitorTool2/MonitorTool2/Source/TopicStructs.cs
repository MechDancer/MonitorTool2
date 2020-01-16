using System.Collections.Generic;
using System.Numerics;
using Windows.UI;

namespace MonitorTool2 {
    /// <summary>
    /// 话题存根
    /// </summary>
    internal class TopicStub {
        public readonly string Remote;
        public readonly ITopicNode Core;

        public TopicStub(string remote, ITopicNode core) {
            Remote = remote;
            Core = core;
        }
        public override string ToString() => $"{Remote}-{Core.Name}";
    }

    /// <summary>
    /// 话题数据缓存
    /// </summary>
    internal interface ITopicMemory {
        public Color Color { get; }
        public bool Connect { get; }
        public float Radius { get; }
    }

    internal abstract class TopicMemoryBase<T> : ITopicMemory where T : struct {
        public Color Color { get; }
        public bool Connect { get; }
        public float Radius { get; }
        public List<List<T>> Data { get; }
        protected TopicMemoryBase(Color color, bool connect, float radius, List<List<T>> data) {
            Color = color;
            Connect = connect;
            Radius = radius;
            Data = data;
        }
        public void Deconstruct(out Color color, out bool connect, out float radius, out List<List<T>> data) {
            color = Color;
            connect = Connect;
            radius = Radius;
            data = Data;
        }
    }

    internal class TopicMemory1 : TopicMemoryBase<Vector2> {
        public TopicMemory1(Color color, bool connect, float radius, List<List<Vector2>> data) 
            : base(color, connect, radius, data) { }
    }

    internal class TopicMemory2 : TopicMemoryBase<Vector3> {
        public TopicMemory2(Color color, bool connect, float radius, List<List<Vector3>> data)
            : base(color, connect, radius, data) { }
    }

    internal class TopicMemory3 : TopicMemoryBase<Vector3> {
        public TopicMemory3(Color color, bool connect, float radius, List<List<Vector3>> data)
             : base(color, connect, radius, data) { }
    }
}
