using MechDancer.Common;
using MechDancer.Framework.Net.Presets;
using MechDancer.Framework.Net.Protocol;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml.Media;

namespace MonitorTool2 {
    internal static class Global {
        public static readonly DateTime T0 = DateTime.Now;

        private static readonly SolidColorBrush
            NoneBrush = new SolidColorBrush(Colors.DarkRed),
            SubscribedBrush = new SolidColorBrush(Colors.Goldenrod),
            ActiveBrush = new SolidColorBrush(Colors.LawnGreen);

        public static SolidColorBrush StateBrush(this TopicState state) =>
            state switch
            {
                TopicState.None => NoneBrush,
                TopicState.Subscribed => SubscribedBrush,
                TopicState.Active => ActiveBrush,
                _ => null
            };
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
        private readonly DateTime _t0;
        private readonly Dictionary<string, Accumulator<Vector2>> _topics;

        public override byte Dim => 1;

        public override ObservableCollection<ITopicNode> Topics { get; }
        public Dimension1Node(DateTime t0) {
            _t0 = t0;
            _topics = new Dictionary<string, Accumulator<Vector2>>();
            Topics = new ObservableCollection<ITopicNode>();
        }
        public override void Receive(string name, bool dir, bool frame, MemoryStream stream) {
            Debug.Assert(!dir);
            Debug.Assert(!frame);
            if (!_topics.TryGetValue(name, out var accumulator)) {
                accumulator = new Accumulator<Vector2>(name, dir);
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
        private readonly Dictionary<string, Accumulator<Vector3>> _accumulators;
        private readonly Dictionary<string, Frame<Vector3>> _frames;

        public override byte Dim => 2;

        public override ObservableCollection<ITopicNode> Topics { get; }
        public Dimension2Node() {
            _accumulators = new Dictionary<string, Accumulator<Vector3>>();
            _frames = new Dictionary<string, Frame<Vector3>>();
            Topics = new ObservableCollection<ITopicNode>();
        }
        public override void Receive(string name, bool dir, bool frame, MemoryStream stream) {
            if (frame) {
                if (!_frames.TryGetValue(name, out var frameNode)) {
                    frameNode = new Frame<Vector3>(name, dir);
                    _frames.Add(name, frameNode);
                    Topics.Add(frameNode);
                    return;
                } else if (frameNode.State == TopicState.None)
                    return;
                Task.Run(() => frameNode.Receive(Parse(dir, stream)));
            } else {
                if (!_accumulators.TryGetValue(name, out var accumulator)) {
                    accumulator = new Accumulator<Vector3>(name, dir);
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
        private static readonly Vector3 NaNVector = new Vector3(float.NaN, float.NaN, float.NaN);

        private readonly Dictionary<string, Accumulator<(Vector3, Vector3)>> _accumulators;
        private readonly Dictionary<string, Frame<(Vector3, Vector3)>> _frames;

        public override byte Dim => 3;

        public override ObservableCollection<ITopicNode> Topics { get; }
        public Dimension3Node() {
            _accumulators = new Dictionary<string, Accumulator<(Vector3, Vector3)>>();
            _frames = new Dictionary<string, Frame<(Vector3, Vector3)>>();
            Topics = new ObservableCollection<ITopicNode>();
        }
        public override void Receive(string name, bool dir, bool frame, MemoryStream stream) {

            if (frame) {
                if (!_frames.TryGetValue(name, out var frameNode)) {
                    frameNode = new Frame<(Vector3, Vector3)>(name, dir);
                    _frames.Add(name, frameNode);
                    Topics.Add(frameNode);
                    return;
                } else if (frameNode.State == TopicState.None)
                    return;
                Task.Run(() => frameNode.Receive(Parse(dir, stream)));
            } else {
                if (!_accumulators.TryGetValue(name, out var accumulator)) {
                    accumulator = new Accumulator<(Vector3, Vector3)>(name, dir);
                    _accumulators.Add(name, accumulator);
                    Topics.Add(accumulator);
                    return;
                } else if (accumulator.State == TopicState.None)
                    return;
                Task.Run(() => accumulator.Receive(Parse(dir, stream)));
            }
        }
        private static (Vector3, Vector3)[] Parse(bool dir, MemoryStream stream) {
            var reader = new NetworkDataReader(stream);
            (Vector3, Vector3)[] buffer;
            if (dir) {
                var count = (stream.Length - stream.Position) / (6 * sizeof(float));
                buffer = new (Vector3, Vector3)[count];
                for (var i = 0; i < count; ++i)
                    buffer[i] = (new Vector3(reader.ReadFloat(),
                                             reader.ReadFloat(),
                                             reader.ReadFloat()),
                                 new Vector3(reader.ReadFloat(),
                                             reader.ReadFloat(),
                                             reader.ReadFloat()));
            } else {
                var count = (stream.Length - stream.Position) / (3 * sizeof(float));
                buffer = new (Vector3, Vector3)[count];
                for (var i = 0; i < count; ++i)
                    buffer[i] = (new Vector3(reader.ReadFloat(),
                                             reader.ReadFloat(),
                                             reader.ReadFloat()),
                                 NaNVector);
            }
            return buffer;
        }
    }

    public enum TopicState { None, Subscribed, Active }
    public interface ITopicNode {
        string Name { get; }
        TopicState State { get; }
        IEnumerable Data { get; }

        void SetLevel(GraphicViewModel source, TopicState level);
        void Clear();
    }

    /// <summary>
    /// 累积模式
    /// </summary>
    public abstract class AccumulatorNodeBase : BindableBase, ITopicNode {
        private readonly HashSet<GraphicViewModel>
            _subscribers = new HashSet<GraphicViewModel>(),
            _observers = new HashSet<GraphicViewModel>();

        private int _capacity = 1000;
        private TopicState _state = TopicState.None;

        public string Name { get; }
        public TopicState State {
            get => _state;
            private set => SetProperty(ref _state, value);
        }
        public int Capacity {
            get => _capacity;
            set => SetProperty(ref _capacity, value);
        }
        public abstract IEnumerable Data { get; }

        protected AccumulatorNodeBase(string name, bool dir)
            => Name = $"[{(dir ? "位姿" : "位置")}][点]{name}";
        protected void Paint() {
            foreach (var observer in _observers)
                observer.Paint();
        }

        public void SetLevel(GraphicViewModel source, TopicState level) {
            switch (level) {
                case TopicState.None:
                    if (_subscribers.Remove(source))
                        _observers.Remove(source);
                    break;
                case TopicState.Subscribed:
                    if (!_observers.Remove(source))
                        _subscribers.Add(source);
                    break;
                case TopicState.Active:
                    if (_observers.Add(source))
                        _subscribers.Add(source);
                    break;
            }
            if (_observers.Any())
                State = TopicState.Active;
            else if (_subscribers.Any())
                State = TopicState.Subscribed;
            else
                State = TopicState.None;
        }
        public abstract void Clear();
    }

    /// <summary>
    /// 帧模式
    /// </summary>
    public abstract class FrameNodeBase : BindableBase, ITopicNode {
        private readonly HashSet<GraphicViewModel>
           _subscribers = new HashSet<GraphicViewModel>(),
           _observers = new HashSet<GraphicViewModel>();

        private TopicState _state = TopicState.None;

        public string Name { get; }
        public TopicState State {
            get => _state;
            private set => SetProperty(ref _state, value);
        }
        public abstract IEnumerable Data { get; }

        protected FrameNodeBase(string name, bool dir)
            => Name = $"[{(dir ? "位姿" : "位置")}][帧]{name}";
        protected void Paint() {
            foreach (var observer in _observers)
                observer.Paint();
        }

        public void SetLevel(GraphicViewModel source, TopicState level) {
            switch (level) {
                case TopicState.None:
                    if (_subscribers.Remove(source))
                        _observers.Remove(source);
                    break;
                case TopicState.Subscribed:
                    if (!_observers.Remove(source))
                        _subscribers.Add(source);
                    break;
                case TopicState.Active:
                    if (_observers.Add(source))
                        _subscribers.Add(source);
                    break;
            }
            if (_observers.Any())
                State = TopicState.Active;
            else if (_subscribers.Any())
                State = TopicState.Subscribed;
            else
                State = TopicState.None;
        }
        public abstract void Clear();
    }

    public class Accumulator<T> : AccumulatorNodeBase where T : struct {
        private readonly List<T> _data = new List<T>();

        public override IEnumerable Data => _data;
        public Accumulator(string name, bool dir) : base(name, dir) { }
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

    public class Frame<T> : FrameNodeBase where T : struct {
        private readonly List<T> _data = new List<T>();

        public override IEnumerable Data => _data;
        public Frame(string name, bool dir) : base(name, dir) { }
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
}
