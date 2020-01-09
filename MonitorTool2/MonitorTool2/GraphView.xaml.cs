using MechDancer.Common;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using Windows.UI;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace MonitorTool2 {
    /// <summary>
    /// 画图控件
    /// </summary>
    public sealed partial class GraphView {
        private GraphicViewModel _viewModel { get; }
        private ObservableCollection<TopicStub> _allTopics { get; }
            = new ObservableCollection<TopicStub>();

        public GraphView(GraphicViewModel model) {
            InitializeComponent();
            _viewModel = model;
            model.SetControl(MainCanvas);
        }

        private void CanvasControl_Draw(CanvasControl sender, CanvasDrawEventArgs args) {
            var buffer = new List<Tuple<Color, List<List<Vector3>>>>();
            // 工作范围
            var outlineAll = _viewModel.AutoRange ? new Outline() : (Outline?)null;
            var outlineFrame = _viewModel.AutoMove ? new Outline() : (Outline?)null;
            // 抄录所有点，同时确定工作范围
            foreach (var topic in from topic in _viewModel.Topics
                                  where topic.Active
                                  select topic) {
                var frame = topic.FrameMode;
                var background = topic.Background;

                Vector2? last = null;
                var group = new List<Vector3>();
                var groups = new List<List<Vector3>>();
                // 遍历抄录点
                foreach (var p in topic.Data.OfType<Vector3>()) {
                    if (float.IsNaN(p.X)) {
                        // 处理分隔符
                        if (group.Any()) {
                            groups.Add(group);
                            group = new List<Vector3>();
                        }
                    } else {
                        // 处理点
                        group.Add(p);
                        last = new Vector2(p.X, p.Y);

                        if (background) continue;
                        if (outlineAll.HasValue)
                            outlineAll.Value.Add(last.Value);
                        else if (outlineFrame != null && frame)
                            outlineFrame.Value.Add(last.Value);
                    }
                }
                // 添加到组
                if (!frame && last.HasValue) outlineFrame?.Add(last.Value);
                if (group.Any()) groups.Add(group);
                if (groups.Any()) buffer.Add(Tuple.Create(topic.Color, groups));
            }
            // 没有任何点直接退出
            if (buffer.None()) return;
            // 笔刷
            var brush = args.DrawingSession;
            // 画布尺寸
            var width = (float)sender.ActualWidth;
            var height = (float)sender.ActualHeight;
            // 自动范围
            if (outlineAll.HasValue) {
                // 范围太小？
                var tooSmall = outlineAll.Value.IsTooSmall(out var optimized);
                // 更新范围
                _viewModel.Outline = optimized.Tile(width, height);
                // 范围太小，画出一个点
                if (tooSmall) {
                    var c = new Vector2(width / 2, height / 2);
                    foreach (var (color, _) in buffer)
                        brush.DrawCircle(c, 1, color, 2);
                    return;
                }
            }
            // 自动移动
            else if (outlineFrame.HasValue) {
                throw new NotImplementedException();
            }
            //TODO else 其他更新范围方式
        }
        private void TopicListSelectionChanged(object sender, SelectionChangedEventArgs e) {
            e.AddedItems
                .OfType<TopicStub>()
                .SingleOrDefault()
                ?.Let(it => new TopicViewModel(it, _viewModel))
                ?.TakeUnless(_viewModel.Topics.Contains)
                ?.Also(_viewModel.Topics.Add);
        }
        private void Flyout_Opening(object sender, object e) {
            _allTopics.Clear();
            foreach (var remote in from _group in MainPage.Groups
                                   from remote in _group.Remotes
                                   select remote) {
                var host = remote.Name;
                foreach (var topic in from dim in remote.Dimensions
                                      where dim.Dim == _viewModel.Dim
                                      from topic in dim.Topics
                                      select new TopicStub(host, topic))
                    if (_viewModel.Topics.None(it => it.CheckEquals(topic)))
                        _allTopics.Add(topic);
            }
        }
    }

    internal struct Outline {
        private float _x0, _x1, _y0, _y1;

        public Outline(float x0 = float.PositiveInfinity,
                       float y0 = float.PositiveInfinity,
                       float x1 = float.NegativeInfinity,
                       float y1 = float.NegativeInfinity) {
            _x0 = x0;
            _y0 = y0;
            _x1 = x1;
            _y1 = y1;
        }

        public (Vector2, Vector2) Range 
            => (new Vector2(_x0, _y0), new Vector2(_x1, _y1));
        public void Add(Vector2 p) {
            if (p.X < _x0) _x0 = p.X;
            if (p.X > _x1) _x1 = p.X;

            if (p.Y < _y0) _y0 = p.Y;
            if (p.Y > _y1) _y1 = p.Y;
        }

        /// <summary>
        /// 范围是否过小
        /// </summary>
        /// <param name="optimized">若原范围过小，以原范围为中心，2x2 的范围，否则为原范围</param>
        /// <returns>范围是否过小</returns>
        public bool IsTooSmall(out Outline optimized) {
            var (a, b) = Range;
            if ((a - b).Length() < 1E-6) {
                var c = (a + b) / 2;
                a = c - One;
                b = c + One;
                optimized = new Outline(a.X, a.Y, b.X, b.Y);
                return true;
            }
            optimized = this;
            return false;
        }

        /// <summary>
        /// 平铺在面区域上的合适范围
        /// </summary>
        /// <param name="width">区域宽度</param>
        /// <param name="height">区域高度</param>
        /// <returns>合适范围</returns>
        public Outline Tile(float width, float height) {
            var w = _x1 - _x0;
            var h = _y1 - _y0;
            // 计算宽高比
            var target = width / height;
            var current = w / h;
            // 修正宽高比
            if (target > current) {
                var e = (w / target - h) / 2;
                return new Outline(_x0, _y0 - e, _x1, _y1 + e);
            } else {
                var e = (h * target - w) / 2;
                return new Outline(_x0 - e, _y0, _x1 + e, _y1);
            }
        }

        private static readonly Vector2 One = new Vector2(1, 1);
    }

    /// <summary>
    /// 图模型
    /// </summary>
    public class GraphicViewModel : BindableBase {
        public ObservableCollection<TopicViewModel> Topics { get; }
            = new ObservableCollection<TopicViewModel>();

        private CanvasControl _canvas;
        private Color _background = Colors.Transparent;
        private bool _locked = false,
                     _axisEquals,
                     _autoRange = true,
                     _autoMove = false;

        public GraphicViewModel(string name, byte dim) {
            Name = name;
            Dim = dim;
            _axisEquals = dim > 1;
        }

        public string Name { get; }
        public byte Dim { get; }
        public Color BackGround {
            get => _background;
            set {
                if (!SetProperty(ref _background, value)) return;
                if (_canvas != null) _canvas.ClearColor = value;
            }
        }
        public bool IsLocked {
            get => _locked;
            set => SetProperty(ref _locked, value);
        }
        public bool AxisEquals {
            get => _axisEquals;
            set => SetProperty(ref _axisEquals, value);
        }
        public bool AutoRange {
            get => _autoRange;
            set {
                if (SetProperty(ref _autoRange, value) && value)
                    AutoMove = false;
            }
        }
        public bool AutoMove {
            get => _autoMove;
            set {
                if (SetProperty(ref _autoMove, value) && value)
                    AutoRange = false;
            }
        }
        internal Outline Outline { get; set; }

        public void SetControl(CanvasControl canvas) {
            _canvas = canvas?.Also(it => it.ClearColor = _background);
        }
        public void Resume() {
            foreach (var topic in Topics) topic.IsPaused = false;
        }
        public void Pause() {
            foreach (var topic in Topics) topic.IsPaused = true;
        }
        public void Close() {
            _canvas = null;
            foreach (var topic in Topics) topic.Close();
        }
        public void Paint() => _canvas?.Invalidate();

        public static SolidColorBrush Brushify(Color color)
           => new SolidColorBrush(color);
    }

    /// <summary>
    /// 话题存根
    /// </summary>
    public class TopicStub {
        public readonly string Remote;
        public readonly ITopicNode Core;

        public TopicStub(string remote, ITopicNode core) {
            Remote = remote;
            Core = core;
        }
        public override string ToString() => $"{Remote}-{Core.Name}";
    }

    /// <summary>
    /// 话题模型
    /// </summary>
    public class TopicViewModel : BindableBase {
        private readonly string _remote;
        private readonly ITopicNode _core;
        private readonly GraphicViewModel _graph;

        private Color _color = NewRandomColor();
        private bool _active = true,
                     _pause = false,
                     _background = false,
                     _connect = false;

        public string Title => $"{_remote}-{_core.Name}";
        public bool FrameMode => _core is FrameNodeBase;
        public Color Color {
            get => _color;
            set {
                if (SetProperty(ref _color, value))
                    _graph.Paint();
            }
        }
        public bool Active {
            get => _active;
            set {
                if (SetProperty(ref _active, value))
                    _core.SetLevel(_graph, _active && !_pause ? TopicState.Active : TopicState.Subscribed);
            }
        }
        public bool IsPaused {
            get => _pause;
            set {
                _pause = value;
                _core.SetLevel(_graph, _active && !_pause ? TopicState.Active : TopicState.Subscribed);
            }
        }
        public bool Background {
            get => _background;
            set {
                if (SetProperty(ref _background, value))
                    _graph.Paint();
            }
        }
        public bool Connect {
            get => _connect;
            set {
                if (SetProperty(ref _connect, value))
                    _graph.Paint();
            }
        }

        public IEnumerable Data => _core.Data;

        public TopicViewModel(TopicStub stub, GraphicViewModel graph) {
            _remote = stub.Remote;
            _core = stub.Core;
            _graph = graph;
            _core.SetLevel(_graph, TopicState.Active);
        }
        public bool CheckEquals(TopicStub stub)
            => _remote == stub.Remote && _core.Name == stub.Core.Name;
        public void Close()
            => _core.SetLevel(_graph, TopicState.None);

        public override string ToString() => Title;
        public override bool Equals(object obj)
            => this == obj || Title == (obj as TopicViewModel)?.Title;
        public override int GetHashCode()
            => Title.GetHashCode();

        private static readonly Random _engine = new Random();
        private static Color NewRandomColor() {
            var rgb = new byte[3];
            _engine.NextBytes(rgb);
            return Color.FromArgb(255, rgb[0], rgb[1], rgb[2]);
        }
    }
}
