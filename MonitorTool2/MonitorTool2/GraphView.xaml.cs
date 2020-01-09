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
            // 工作范围
            var outlineAll = _viewModel.AutoRange ? new Outline() : (Outline?)null;
            var outlineFrame = _viewModel.AutoMove ? new Outline() : (Outline?)null;
            // 抄录所有点，同时确定工作范围
            IEnumerable<List<Vector3>> Split(TopicViewModel topic) {
                // 取出迭代器，没有任何点则直接退出
                var itor = topic.Data.OfType<Vector3>().GetEnumerator();
                if (!itor.MoveNext()) yield break;
                // 初始化末有效点存储
                var last = (Vector3?)null;
                // 否则按分隔符划分数据
                IEnumerable<Vector3> Accumulate() {
                    while (true) {
                        var p = itor.Current;
                        // 对于分隔符，由外部控制迭代器移动
                        if (float.IsNaN(p.X)) yield break;
                        // 非背景模式下，控制自动范围
                        if (!topic.Background) {
                            if (outlineAll.HasValue)
                                outlineAll.Value.Add(p.X, p.Y);
                            else if (outlineFrame.HasValue && topic.FrameMode)
                                outlineFrame.Value.Add(p.X, p.Y);
                        }
                        // 存储最末有效点
                        last = p;
                        yield return p;
                        // 迭代器移动，失败直接退出
                        if (!itor.MoveNext()) yield break;
                    }
                }
                // 执行划分
                while (true) {
                    var group = Accumulate().ToList();
                    if (group.Any()) yield return group;
                    // 迭代器移动，失败直接退出
                    if (!itor.MoveNext()) {
                        if (!topic.Background // 背景模式，跳过
                         && !topic.FrameMode  // 帧模式，所有有效点已经计算过
                         && !last.HasValue) { // 不存在有效点，跳过
                            var p = last.Value;
                            outlineFrame?.Add(p.X, p.Y);
                        }
                        yield break;
                    }
                }
            }
            // 执行抄录
            var topics = (from topic in _viewModel.Topics
                          where topic.Active
                          let @group = Split(topic).ToList()
                          where @group.Any()
                          select Tuple.Create(topic.Color, @group)
                         ).ToList();
            // 没有任何点直接退出
            if (topics.None()) return;
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
                // 范围太小，其实只有一个点，画出即可
                if (tooSmall) {
                    var c = new Vector2(width / 2, height / 2);
                    foreach (var (color, _) in topics)
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
            foreach (var topic in from _group in MainPage.Groups
                                  from remote in _group.Remotes
                                  from dim in remote.Dimensions
                                  where dim.Dim == _viewModel.Dim
                                  from topic in dim.Topics
                                  let stub = new TopicStub(remote.Name, topic)
                                  where _viewModel.Topics.None(it => it.CheckEquals(stub))
                                  select stub)
                _allTopics.Add(topic);
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
        public void Add(float x, float y) {
            if (x < _x0) _x0 = x;
            if (x > _x1) _x1 = x;

            if (y < _y0) _y0 = y;
            if (y > _y1) _y1 = y;
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
