using MechDancer.Common;
using MechDancer.Framework.Net.Presets;
using MechDancer.Framework.Net.Protocol;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Numerics;

namespace MonitorTool2 {
    internal static class Global {
        public static readonly DateTime T0 = DateTime.Now;
    }

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
                new Dimension1Node(Global.T0),
                new Dimension2Node(),
                new Dimension3Node(),
            };
        public string Name { get; }
        public RemoteNode(string name, GroupNode group) {
            Name = name;
            _group = group;
        }
        public void Receive(byte[] payload) {
            using var stream = new MemoryStream(payload);
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

        public abstract ObservableCollection<TopicNode> Topics { get; }
        public string Title => $"{Dim} 维信号";
        public abstract void Receive(string name, bool dir, bool frame, MemoryStream stream);
    }

    /// <summary>
    /// 1 维节点
    /// </summary>
    public class Dimension1Node : DimensionNodeBase {
        private readonly DateTime _t0;
        private readonly Dictionary<string, Accumulator<Vector2>> _topics;

        public override byte Dim => 1;

        public override ObservableCollection<TopicNode> Topics { get; }
        public Dimension1Node(DateTime t0) {
            _t0 = t0;
            _topics = new Dictionary<string, Accumulator<Vector2>>();
            Topics = new ObservableCollection<TopicNode>();
        }
        public override void Receive(string name, bool dir, bool frame, MemoryStream stream) {
            Debug.Assert(!dir);
            Debug.Assert(!frame);
            if (!_topics.TryGetValue(name, out var accumulator)) {
                accumulator = new Accumulator<Vector2>(name, dir);
                _topics.Add(name, accumulator);
                Topics.Add(accumulator);
            }
        }
    }

    /// <summary>
    /// 2 维节点
    /// </summary>
    public class Dimension2Node : DimensionNodeBase {
        private readonly Dictionary<string, Accumulator<Vector3>> _accumulators;
        private readonly Dictionary<string, Frame<Vector3>> _frames;

        public override byte Dim => 2;

        public override ObservableCollection<TopicNode> Topics { get; }
        public Dimension2Node() {
            _accumulators = new Dictionary<string, Accumulator<Vector3>>();
            _frames = new Dictionary<string, Frame<Vector3>>();
            Topics = new ObservableCollection<TopicNode>();
        }
        public override void Receive(string name, bool dir, bool frame, MemoryStream stream) {
            if (frame) {
                if (!_frames.TryGetValue(name, out var frameNode)) {
                    frameNode = new Frame<Vector3>(name, dir);
                    _frames.Add(name, frameNode);
                    Topics.Add(frameNode);
                }
            } else {
                if (!_accumulators.TryGetValue(name, out var accumulator)) {
                    accumulator = new Accumulator<Vector3>(name, dir);
                    _accumulators.Add(name, accumulator);
                    Topics.Add(accumulator);
                }
            }
        }
    }

    /// <summary>
    /// 3 维节点
    /// </summary>
    public class Dimension3Node : DimensionNodeBase {
        private readonly Dictionary<string, Accumulator<(Vector3, Vector3)>> _accumulators;
        private readonly Dictionary<string, Frame<(Vector3, Vector3)>> _frames;

        public override byte Dim => 3;

        public override ObservableCollection<TopicNode> Topics { get; }
        public Dimension3Node() {
            _accumulators = new Dictionary<string, Accumulator<(Vector3, Vector3)>>();
            _frames = new Dictionary<string, Frame<(Vector3, Vector3)>>();
            Topics = new ObservableCollection<TopicNode>();
        }
        public override void Receive(string name, bool dir, bool frame, MemoryStream stream) {
            if (frame) {
                if (!_frames.TryGetValue(name, out var frameNode)) {
                    frameNode = new Frame<(Vector3, Vector3)>(name, dir);
                    _frames.Add(name, frameNode);
                    Topics.Add(frameNode);
                }
            } else {
                if (!_accumulators.TryGetValue(name, out var accumulator)) {
                    accumulator = new Accumulator<(Vector3, Vector3)>(name, dir);
                    _accumulators.Add(name, accumulator);
                    Topics.Add(accumulator);
                }
            }
        }
    }

    public interface TopicNode {
        string Name { get; }
    }

    /// <summary>
    /// 累积模式
    /// </summary>
    public abstract class AccumulatorNodeBase : TopicNode {
        public string Name { get; }
        public ViewModel Model { get; } = new ViewModel();
        public AccumulatorNodeBase(string name, bool dir)
            => Name = $"[{(dir ? "位姿" : "位置")}][点]{name}";
        public class ViewModel : BindableBase {
            private uint _capacity = 1000;
            public uint Capacity {
                get => _capacity;
                set => SetProperty(ref _capacity, value);
            }
        }
    }

    /// <summary>
    /// 帧模式
    /// </summary>
    public abstract class FrameNodeBase : TopicNode {
        public string Name { get; }
        public FrameNodeBase(string name, bool dir)
            => Name = $"[{(dir ? "位姿" : "位置")}][帧]{name}";
    }

    public class Accumulator<T> : AccumulatorNodeBase where T : struct {
        private readonly List<T> _data = new List<T>();
        public IReadOnlyList<T> Data => _data;
        public Accumulator(string name, bool dir) : base(name, dir) { }
        public void Receive(List<T> data) {
        }
    }

    public class Frame<T> : FrameNodeBase where T : struct {
        public IReadOnlyList<T> Data { get; private set; }
        public Frame(string name, bool dir) : base(name, dir) {
            Data = new List<T>();
        }
        public void Receive(List<T> data) {
        }
    }
}
