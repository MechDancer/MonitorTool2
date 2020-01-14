using MechDancer.Common;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Windows.UI;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace MonitorTool2 {
    /// <summary>
    /// 画图控件
    /// </summary>
    public sealed partial class GraphView {
        private static readonly CanvasTextFormat TextFormat
            = new CanvasTextFormat { FontSize = 16 };
        public float BlankBorderWidth { get; set; } = 8;

        private List<TopicMemory> _memory = new List<TopicMemory>();
        private Vector2? _pointer = null, _pressed = null, _released = null;
        private GraphicViewModel _viewModel { get; }
        private ObservableCollection<TopicStub> _allTopics { get; }
            = new ObservableCollection<TopicStub>();

        public GraphView(GraphicViewModel model) {
            InitializeComponent();
            _viewModel = model;
            model.SetControl(MainCanvas);
        }

        private void CanvasControl_Draw(CanvasControl sender, CanvasDrawEventArgs args) {
            // 笔刷
            var brush = args.DrawingSession;
            // 画布尺寸
            var canvasW = (float)sender.ActualWidth;
            var canvasH = (float)sender.ActualHeight;
            // 工作范围
            var auto = _viewModel.AutoRange && !_released.HasValue;
            var areaX = auto && _viewModel.AutoWidthAll ? Area.Init() : (Area?)null;
            var areaY = auto && _viewModel.AutoHeightAll ? Area.Init() : (Area?)null;
            var areaXFrame = auto && _viewModel.AutoWidthFrame ? Area.Init() : (Area?)null;
            var areaYFrame = auto && _viewModel.AutoHeightFrame ? Area.Init() : (Area?)null;
            // 刷新工作范围
            if (!_viewModel.IsLocked) {
                // 筛选活跃话题
                var actives = _viewModel.Topics.Where(it => it.Active).ToList();
                if (actives.None()) return;
                // 从话题缓存抄录所有点，同时确定工作范围
                IEnumerable<List<Vector3>> Split(TopicViewModel topic) {
                    lock (topic.Data) {
                        // 取出迭代器，没有任何点则直接退出
                        var itor = topic.Data.OfType<Vector3>().GetEnumerator();
                        if (!itor.MoveNext()) yield break;
                        // 初始化末有效点存储
                        var last = (Vector2?)null;
                        // 否则按分隔符划分数据
                        IEnumerable<Vector3> Accumulate() {
                            while (true) {
                                var p = itor.Current;
                                last = new Vector2(p.X, p.Y);
                                // 对于分隔符，由外部控制迭代器移动
                                if (float.IsNaN(p.X)) yield break;
                                // 非背景模式下，控制自动范围
                                if (!topic.Background) {
                                    areaX += p.X;
                                    areaY += p.Y;
                                    if (topic.FrameMode) {
                                        areaXFrame += p.X;
                                        areaYFrame += p.Y;
                                    }
                                }
                                // 存储最末有效点
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
                                if (!topic.Background // 非背景模式
                                 && !topic.FrameMode  // 非帧模式（帧模式下所有有效点已经计算过）
                                 && last.HasValue     // 存在有效点
                                ) {
                                    areaXFrame += last?.X;
                                    areaYFrame += last?.Y;
                                }
                                yield break;
                            }
                        }
                    }
                }
                // 执行抄录
                _memory = (from topic in actives
                           let data = Split(topic).ToList()
                           where data.Any()
                           select new TopicMemory(topic.Color, topic.Connect, data)
                          ).ToList();
            }
            // 没有任何点直接退出
            if (_memory.None()) {
                // 绘制标尺
                if (_pointer.HasValue) {
                    var x0 = _pointer.Value.X;
                    var y0 = _pointer.Value.Y;
                    brush.DrawLine(new Vector2(0, y0 + 1), new Vector2(canvasW, y0 + 1), Colors.Black);
                    brush.DrawLine(new Vector2(x0 + 1, 0), new Vector2(x0 + 1, canvasH), Colors.Black);
                    brush.DrawLine(new Vector2(x0, 0), new Vector2(x0, canvasH), Colors.White);
                    brush.DrawLine(new Vector2(0, y0), new Vector2(canvasW, y0), Colors.White);
                    if (_pressed.HasValue) {
                        var x1 = _pressed.Value.X;
                        var y1 = _pressed.Value.Y;
                        brush.DrawLine(new Vector2(x0, y1 + 1), new Vector2(x1, y1 + 1), Colors.Black);
                        brush.DrawLine(new Vector2(x1 + 1, y0), new Vector2(x1 + 1, y1), Colors.Black);
                        brush.DrawLine(new Vector2(x0, y1), new Vector2(x1, y1), Colors.White);
                        brush.DrawLine(new Vector2(x1, y0), new Vector2(x1, y1), Colors.White);
                        if (_released.HasValue) _pressed = _released = null;
                    }
                }
                return;
            }
            // 显示范围
            var width = canvasW - 2 * BlankBorderWidth;
            var height = canvasH - 2 * BlankBorderWidth;
            if (areaX.HasValue || areaXFrame.HasValue
             || areaY.HasValue || areaYFrame.HasValue) {
                _viewModel.CurrentRange(width, height, out var currentX, out var currentY);
                // 更新范围
                _viewModel.RangeX =
                    areaX?.Determine(currentX, _viewModel.AllowWidthShrink)
                 ?? areaXFrame?.Determine(currentX, _viewModel.AllowWidthShrink)
                 ?? currentX;
                _viewModel.RangeY =
                    areaY?.Determine(currentY, _viewModel.AllowHeightShrink)
                 ?? areaYFrame?.Determine(currentY, _viewModel.AllowHeightShrink)
                 ?? currentY;
            } else if (_released.HasValue) {
                _viewModel.AutoRange = false;
                _viewModel.Transform(canvasW, canvasH, out _, out var tf);
                var a = tf(_pressed.Value);
                var b = tf(_released.Value);
                _viewModel.CurrentRange(width, height, out var currentX, out var currentY);
                _viewModel.RangeX = Area.Auto(a.X, b.X).Determine(currentX, _viewModel.AllowWidthShrink);
                _viewModel.RangeY = Area.Auto(a.Y, b.Y).Determine(currentY, _viewModel.AllowHeightShrink);
                _pressed = _released = null;
            }
            // 根据范围计算变换
            _viewModel.Transform(canvasW, canvasH, out var transform, out var inverse);
            // 在缓存上迭代
            foreach (var (color, connect, topic) in _memory) {
                foreach (var group in topic) {
                    Vector2 current = default;
                    var notFirst = false;
                    foreach (var pose in group) {
                        // 更新缓存
                        var last = current;
                        current = transform(new Vector2(pose.X, pose.Y));
                        // 画点
                        brush.DrawCircle(current.X, current.Y, 1, color);
                        // 画线
                        if (notFirst && connect)
                            brush.DrawLine(last, current, color);
                        else
                            notFirst = true;
                        // 画姿态
                        if (!float.IsNaN(pose.Z))
                            brush.DrawLine(current, current + new Vector2(MathF.Cos(pose.Z), -MathF.Sin(pose.Z)) * 10, color);
                    }
                }
            }
            // 绘制标尺
            if (_pointer.HasValue) {
                var x0 = _pointer.Value.X;
                var y0 = _pointer.Value.Y;
                var p = inverse(_pointer.Value);
                brush.DrawLine(new Vector2(0, y0 + 1), new Vector2(canvasW, y0 + 1), Colors.Black);
                brush.DrawLine(new Vector2(x0 + 1, 0), new Vector2(x0 + 1, canvasH), Colors.Black);
                brush.DrawLine(new Vector2(x0, 0), new Vector2(x0, canvasH), Colors.White);
                brush.DrawLine(new Vector2(0, y0), new Vector2(canvasW, y0), Colors.White);
                brush.DrawText($"{p.X.ToString("0.000")}, {p.Y.ToString("0.000")}", x0 + 1, y0 - 15, Colors.Black, TextFormat);
                brush.DrawText($"{p.X.ToString("0.000")}, {p.Y.ToString("0.000")}", x0, y0 - 16, Colors.White, TextFormat);
                if (_pressed.HasValue) {
                    var x1 = _pressed.Value.X;
                    var y1 = _pressed.Value.Y;
                    brush.DrawLine(new Vector2(x0, y1 + 1), new Vector2(x1, y1 + 1), Colors.Black);
                    brush.DrawLine(new Vector2(x1 + 1, y0), new Vector2(x1 + 1, y1), Colors.Black);
                    brush.DrawLine(new Vector2(x0, y1), new Vector2(x1, y1), Colors.White);
                    brush.DrawLine(new Vector2(x1, y0), new Vector2(x1, y1), Colors.White);
                }
            }
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
        private void MainCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e) {
            _viewModel.AutoRange = false;

            var canvas = (CanvasControl)sender;
            var pointer = e.GetCurrentPoint(canvas);

            var delta = pointer.Properties.MouseWheelDelta;
            if (delta == 0) return;
            var k = 1 + delta / 480f;

            var w = (float)canvas.ActualWidth - BlankBorderWidth;
            var h = (float)canvas.ActualHeight - BlankBorderWidth;
            var px = (float)pointer.Position.X - BlankBorderWidth;
            var py = (float)pointer.Position.Y - BlankBorderWidth;

            _viewModel.RangeX = _viewModel.RangeX.Let(r => r.Affine(k, r.T0 + r.L * px / w));
            _viewModel.RangeY = _viewModel.RangeY.Let(r => r.Affine(k, r.T0 + r.L * (1 - py / h)));

            MainCanvas.Invalidate();
        }
        private void MainCanvas_PointerMoved(object sender, PointerRoutedEventArgs e) {
            var pointer = e.GetCurrentPoint((CanvasControl)sender);
            _pointer = new Vector2((float)pointer.Position.X, (float)pointer.Position.Y);
            if (pointer.Properties.IsLeftButtonPressed)
                _pressed ??= _pointer;
            else if (_pressed.HasValue)
                _released = _pointer;
            MainCanvas.Invalidate();
        }
        private void MainCanvas_PointerExited(object sender, PointerRoutedEventArgs e) {
            _pointer =
            _pressed =
            _released = null;
            MainCanvas.Invalidate();
        }
    }

    internal struct Area {
        public readonly float T0, T1;
        public float C => (T0 + T1) / 2;
        public float L => T1 - T0;

        public Area(float t0, float t1) {
            T0 = t0;
            T1 = t1;
        }

        public static Area Init()
            => new Area(float.PositiveInfinity, float.NegativeInfinity);

        public static Area operator +(Area area, float value)
            => new Area(Math.Min(area.T0, value), Math.Max(area.T1, value));

        /// <summary>
        /// 更新范围
        /// </summary>
        /// <param name="current">当前范围</param>
        /// <param name="allowShrink">允许收缩</param>
        /// <returns>最终显示范围</returns>
        public Area Determine(
           Area current,
           bool allowShrink
        ) {
            var cl = current.L;
            var tl = L;
            // 视野需要增大 或 视野可收缩
            if (tl >= cl || (allowShrink && tl > 1E-6))
                return this;
            // 视野仅移动
            return T0 < current.T0
                 ? new Area(T0, T0 + cl)
                 : T1 > current.T1
                 ? new Area(T1 - cl, T1)
                 : current;
        }

        public static Area Auto(float t0, float t1) =>
            t0 < t1 ? new Area(t0, t1) : new Area(t1, t0);

        public Area Affine(float k, float b) 
            => new Area(k * (T0 - b) + b, k * (T1 - b) + b);
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
                     _autoWidthAll = true,
                     _autoWidthFrame = false,
                     _allowWidthShrink = true,
                     _autoHeightAll = true,
                     _autoHeightFrame = false,
                     _allowHeightShrink = true,
                     _autoRange = true;
        private Area _rangeX, _rangeY;

        private void ConfigChanged(bool value, Action mutex) {
            Notify(nameof(AutoRangeEnabled));
            if (value) mutex();
            else if (!AutoRangeEnabled) AutoRange = false;
            _canvas.Invalidate();
        }

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
            set {
                if (SetProperty(ref _locked, value))
                    ConfigChanged(!value, () => { });
            }
        }
        public bool AxisEquals {
            get => _axisEquals;
            set => SetProperty(ref _axisEquals, value);
        }
        public bool AutoRangeEnabled
            => !IsLocked
            && (_autoWidthAll
            || _autoWidthFrame
            || _autoHeightAll
            || _autoHeightFrame);
        public bool AutoRange {
            get => _autoRange;
            set {
                if (SetProperty(ref _autoRange, value))
                    _canvas.Invalidate();
            }
        }
        public bool AutoWidthAll {
            get => _autoWidthAll;
            set {
                if (SetProperty(ref _autoWidthAll, value))
                    ConfigChanged(value, () => AutoWidthFrame = false);
            }
        }
        public bool AutoWidthFrame {
            get => _autoWidthFrame;
            set {
                if (SetProperty(ref _autoWidthFrame, value))
                    ConfigChanged(value, () => AutoWidthAll = false);
            }
        }
        public bool AllowWidthShrink {
            get => _allowWidthShrink;
            set => SetProperty(ref _allowWidthShrink, value);
        }
        public bool AutoHeightAll {
            get => _autoHeightAll;
            set {
                if (SetProperty(ref _autoHeightAll, value))
                    ConfigChanged(value, () => AutoHeightFrame = false);
            }
        }
        public bool AutoHeightFrame {
            get => _autoHeightFrame;
            set {
                if (SetProperty(ref _autoHeightFrame, value))
                    ConfigChanged(value, () => AutoHeightAll = false);
            }
        }
        public bool AllowHeightShrink {
            get => _allowHeightShrink;
            set => SetProperty(ref _allowHeightShrink, value);
        }
        public string X0Text => _rangeX.T0.ToString("0.000");
        public string X1Text => _rangeX.T1.ToString("0.000");
        public string Y0Text => _rangeY.T0.ToString("0.000");
        public string Y1Text => _rangeY.T1.ToString("0.000");
        internal Area RangeX {
            get => _rangeX;
            set {
                _rangeX = value;
                Notify(nameof(X0Text));
                Notify(nameof(X1Text));
            }
        }
        internal Area RangeY {
            get => _rangeY;
            set {
                _rangeY = value;
                Notify(nameof(Y0Text));
                Notify(nameof(Y1Text));
            }
        }

        internal void Transform(
            float width,
            float height,
            out Func<Vector2, Vector2> transform,
            out Func<Vector2, Vector2> inverse
        ) {
            var c0 = new Vector2(RangeX.C, RangeY.C);
            var c1 = new Vector2(width, height) / 2;
            var kx = width / RangeX.L;
            var ky = height / RangeY.L;
            Vector2 mirror(Vector2 it) => new Vector2(it.X, height - it.Y);
            if (AxisEquals) {
                var k = Math.Min(kx, ky);
                transform = p => mirror(k * (p - c0) + c1);
                inverse = p => (mirror(p) - c1) / k + c0;
            } else {
                var k = new Vector2(kx, ky);
                transform = p => mirror(k * (p - c0) + c1);
                inverse = p => (mirror(p) - c1) / k + c0;
            }
        }

        internal void CurrentRange(
            float width,
            float height,
            out Area x,
            out Area y
        ) {
            // 当前绘图范围
            x = RangeX;
            y = RangeY;
            // 如果有等轴性，计算实际显示范围
            if (AxisEquals) {
                var xl = x.L;
                var yl = y.L;
                var currentK = xl / yl;
                var actualK = width / height;
                if (actualK > currentK) {
                    var e = (yl * actualK - xl) / 2;
                    x = new Area(x.T0 - e, x.T1 + e);
                } else {
                    var e = (xl / actualK - yl) / 2;
                    y = new Area(y.T0 - e, y.T1 + e);
                }
            }
        }

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

    public class TopicMemory {
        public Color Color { get; }
        public bool Connect { get; }
        public List<List<Vector3>> Data { get; }

        public TopicMemory(Color color, bool connect, List<List<Vector3>> data) {
            Color = color;
            Connect = connect;
            Data = data;
        }
        public void Deconstruct(out Color color, out bool connect, out List<List<Vector3>> data) {
            color = Color;
            connect = Connect;
            data = Data;
        }
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
                if (!SetProperty(ref _active, value)) return;
                _core.SetLevel(_graph, _active && !_pause ? TopicState.Active : TopicState.Subscribed);
                _graph.Paint();
            }
        }
        public bool IsPaused {
            get => _pause;
            set {
                if (!SetProperty(ref _pause, value)) return;
                _core.SetLevel(_graph, _active && !_pause ? TopicState.Active : TopicState.Subscribed);
                _graph.Paint();
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
