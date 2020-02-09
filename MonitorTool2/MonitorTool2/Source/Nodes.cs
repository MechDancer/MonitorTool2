using MechDancer.Common;
using MechDancer.Framework.Net.Presets;
using MechDancer.Framework.Net.Protocol;
using MonitorTool2.Source;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Threading.Tasks;

namespace MonitorTool2 {
    /// <summary>
    /// 组节点
    /// </summary>
    public class GroupNode {
        private readonly Dictionary<string, RemoteNode> _remotes
            = new Dictionary<string, RemoteNode>();

        public ObservableCollection<RemoteNode> Remotes { get; }
            = new ObservableCollection<RemoteNode>();
        public RemoteHub Hub { get; }
        public IPEndPoint Address => Hub.Group;
        public GroupNode(RemoteHub hub) => Hub = hub;
        public void Receive(RemotePacket pack) {
            var (name, cmd, payload) = pack;
            if (string.IsNullOrWhiteSpace(name)) return;
            if (!_remotes.TryGetValue(name, out var node)) {
                node = new RemoteNode(name, this);
                _remotes[name] = node;
                Remotes.Add(node);
            }
            if (cmd == 6) node.Receive(payload);
        }
        public void Remove(RemoteNode node) {
            if (node != null && _remotes.Remove(node.Name))
                Remotes.Remove(node);
        }
    }

    /// <summary>
    /// 远程对象节点
    /// </summary>
    public class RemoteNode {
        private readonly GroupNode _group;

        public List<DimensionNodeBase> Dimensions { get; }
            = new List<DimensionNodeBase>{
                new Dimension1Node(),
                new Dimension2Node(),
                new Dimension3Node(),
            };
        public string Name { get; }
        public RemoteNode(string name, GroupNode group) {
            Name = name;
            _group = group;
        }
        public void Receive(byte[] payload) {
            var stream = new MemoryStream(payload);
            string name;
            byte mode;
            try {
                name = stream.ReadEnd();
                if (string.IsNullOrWhiteSpace(name)) return;
                mode = new NetworkDataReader(stream).ReadByte();
                Dimensions[(mode & 0b0011) - 1].Receive(
                    name: name,
                    dir: (mode & 0b0100) != 0,
                    frame: (mode & 0b1000) != 0,
                    stream: stream);
            } catch (Exception) {
            }
        }
        public void Close() => _group.Remove(this);
    }

    /// <summary>
    /// 维度细分节点
    /// </summary>
    public abstract class DimensionNodeBase {
        public abstract byte Dim { get; }

        public abstract ObservableCollection<ITopicNode> Topics { get; }
        public string Title => $"{Dim} 维信号";
        public abstract void Receive(string name, bool dir, bool frame, MemoryStream stream);
    }

    /// <summary>
    /// 1 维节点
    /// </summary>
    public class Dimension1Node : DimensionNodeBase {
        private static readonly DateTime _t0 = DateTime.Now;
        private readonly Dictionary<string, Accumulator1> _topics
            = new Dictionary<string, Accumulator1>();

        public override byte Dim => 1;
        public override ObservableCollection<ITopicNode> Topics { get; }
            = new ObservableCollection<ITopicNode>();
        public override void Receive(string name, bool dir, bool frame, MemoryStream stream) {
            Debug.Assert(!dir);
            Debug.Assert(!frame);
            if (!_topics.TryGetValue(name, out var accumulator)) {
                accumulator = new Accumulator1(name);
                Topics.Add(accumulator);
                _topics.Add(name, accumulator);
                return;
            } else if (accumulator.State == TopicState.None)
                return;
            Task.Run(() => {
                accumulator.Receive(new Vector2(
                 (float)(DateTime.Now - _t0).TotalMilliseconds,
                 new NetworkDataReader(stream).ReadFloat()));
            });
        }
    }

    /// <summary>
    /// 2 维节点
    /// </summary>
    public class Dimension2Node : DimensionNodeBase {
        private readonly Dictionary<string, Accumulator2> _accumulators
            = new Dictionary<string, Accumulator2>();
        private readonly Dictionary<string, Frame2> _frames
            = new Dictionary<string, Frame2>();

        public override byte Dim => 2;
        public override ObservableCollection<ITopicNode> Topics { get; }
            = new ObservableCollection<ITopicNode>();
        public override void Receive(string name, bool dir, bool frame, MemoryStream stream) {
            if (frame) {
                if (!_frames.TryGetValue(name, out var frameNode)) {
                    frameNode = new Frame2(name);
                    _frames.Add(name, frameNode);
                    Topics.Add(frameNode);
                    return;
                } else if (frameNode.State == TopicState.None)
                    return;
                Task.Run(() => frameNode.Receive(Parse(dir, stream)));
            } else {
                if (!_accumulators.TryGetValue(name, out var accumulator)) {
                    accumulator = new Accumulator2(name);
                    _accumulators.Add(name, accumulator);
                    Topics.Add(accumulator);
                    return;
                } else if (accumulator.State == TopicState.None)
                    return;
                Task.Run(() => accumulator.Receive(Parse(dir, stream)));
            }
        }
        private static Vector3[] Parse(bool dir, MemoryStream stream) {
            var reader = new NetworkDataReader(stream);
            Vector3[] buffer;
            if (dir) {
                var count = (stream.Length - stream.Position) / (3 * sizeof(float));
                buffer = new Vector3[count];
                for (var i = 0; i < count; ++i)
                    buffer[i] = new Vector3(reader.ReadFloat(),
                                            reader.ReadFloat(),
                                            reader.ReadFloat());
            } else {
                var count = (stream.Length - stream.Position) / (2 * sizeof(float));
                buffer = new Vector3[count];
                for (var i = 0; i < count; ++i)
                    buffer[i] = new Vector3(reader.ReadFloat(),
                                            reader.ReadFloat(),
                                            float.NaN);
            }
            return buffer;
        }
    }

    /// <summary>
    /// 3 维节点
    /// </summary>
    public class Dimension3Node : DimensionNodeBase {
        private readonly Dictionary<string, Accumulator3> _accumulators
            = new Dictionary<string, Accumulator3>();
        private readonly Dictionary<string, Frame3> _frames
            = new Dictionary<string, Frame3>();

        public override byte Dim => 3;
        public override ObservableCollection<ITopicNode> Topics { get; }
        public override void Receive(string name, bool dir, bool frame, MemoryStream stream) {
            Debug.Assert(!dir);
            if (frame) {
                if (!_frames.TryGetValue(name, out var frameNode)) {
                    frameNode = new Frame3(name);
                    _frames.Add(name, frameNode);
                    Topics.Add(frameNode);
                    return;
                } else if (frameNode.State == TopicState.None)
                    return;
                Task.Run(() => frameNode.Receive(Parse(stream)));
            } else {
                if (!_accumulators.TryGetValue(name, out var accumulator)) {
                    accumulator = new Accumulator3(name);
                    _accumulators.Add(name, accumulator);
                    Topics.Add(accumulator);
                    return;
                } else if (accumulator.State == TopicState.None)
                    return;
                Task.Run(() => accumulator.Receive(Parse(stream)));
            }
        }
        private static Vector3[] Parse(MemoryStream stream) {
            var reader = new NetworkDataReader(stream);
            Vector3[] buffer;

            var count = (stream.Length - stream.Position) / (3 * sizeof(float));
            buffer = new Vector3[count];
            for (var i = 0; i < count; ++i)
                buffer[i] = new Vector3(reader.ReadFloat(),
                                         reader.ReadFloat(),
                                         reader.ReadFloat());
            return buffer;
        }
    }

    /// <summary>
    /// 话题节点功能接口
    /// </summary>
    public interface ITopicNode {
        /// <summary>
        /// 话题名
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 订阅状态
        /// </summary>
        TopicState State { get; }

        /// <summary>
        /// 通知在某个图中的订阅级别
        /// </summary>
        /// <param name="source">图</param>
        /// <param name="level">级别</param>
        void SetLevel(GraphicViewModel source, TopicState level);

        /// <summary>
        /// 清除数据
        /// </summary>
        void Clear();
    }

    /// <summary>
    /// 话题节点数据接口
    /// </summary>
    /// <typeparam name="T">保存数据类型</typeparam>
    public interface ITopicNode<T> : ITopicNode
        where T : struct {
        IReadOnlyList<T> Data { get; }
    }

    /// <summary>
    /// 累积模式
    /// </summary>
    public abstract class AccumulatorNodeBase
        : BindableBase, ITopicNode {
        private readonly GraphicManager _manager
            = new GraphicManager();

        private int _capacity = 1000;

        public string Name { get; }
        public TopicState State => _manager.State;

        public int Capacity {
            get => _capacity;
            set => SetProperty(ref _capacity, value);
        }

        protected AccumulatorNodeBase(string name) => Name = $"[点]{name}";
        protected void Paint() => _manager.Paint();

        public void SetLevel(GraphicViewModel source, TopicState level)
            => _manager.SetLevel(source, level);
        public abstract void Clear();
    }

    /// <summary>
    /// 帧模式
    /// </summary>
    public abstract class FrameNodeBase
        : BindableBase, ITopicNode {
        private readonly GraphicManager _manager
            = new GraphicManager();

        public string Name { get; }
        public TopicState State => _manager.State;

        protected FrameNodeBase(string name) => Name = $"[帧]{name}";
        protected void Paint() => _manager.Paint();

        public void SetLevel(GraphicViewModel source, TopicState level)
           => _manager.SetLevel(source, level);
        public abstract void Clear();
    }

    public class Accumulator<T>
        : AccumulatorNodeBase, ITopicNode<T>
        where T : struct {
        private readonly List<T> _data = new List<T>();

        public IReadOnlyList<T> Data => _data;
        public Accumulator(string name) : base(name) { }
        public void Receive(params T[] data) {
            lock (_data) {
                if (data.Length >= Capacity) {
                    _data.Clear();
                    _data.AddRange(data.Take(Capacity));
                } else {
                    (_data.Count + data.Length - Capacity)
                        .AcceptIf(it => it > 0)
                        ?.Also(it => _data.RemoveRange(0, Math.Min(_data.Count, it)));
                    _data.AddRange(data);
                }
            }
            Paint();
        }
        public override void Clear() {
            lock (_data) { _data.Clear(); }
            Paint();
        }
    }

    public class Frame<T>
        : FrameNodeBase, ITopicNode<T>
        where T : struct {
        private readonly List<T> _data = new List<T>();

        public IReadOnlyList<T> Data => _data;
        public Frame(string name) : base(name) { }
        public void Receive(params T[] data) {
            lock (_data) {
                _data.Clear();
                _data.AddRange(data);
            }
            Paint();
        }
        public override void Clear() {
            lock (_data) { _data.Clear(); }
            Paint();
        }
    }

    public sealed class Accumulator1 : Accumulator<Vector2> {
        public Accumulator1(string name) : base(name) { }
    }

    public sealed class Accumulator2 : Accumulator<Vector3> {
        public Accumulator2(string name) : base(name) { }
    }

    public sealed class Accumulator3 : Accumulator<Vector3> {
        public Accumulator3(string name) : base(name) { }
    }

    public sealed class Frame2 : Frame<Vector3> {
        public Frame2(string name) : base(name) { }
    }

    public sealed class Frame3 : Frame<Vector3> {
        public Frame3(string name) : base(name) { }
    }
}
