using MechDancer.Common;
using MechDancer.Framework.Net.Resources;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using MonitorTool2.Source;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
    public sealed partial class GraphicView {
        private static readonly CanvasTextFormat _textFormat
            = new CanvasTextFormat { FontSize = 16 };
        public float BlankBorderWidth { get; set; } = 8;

        private List<ITopicMemory> _memory = new List<ITopicMemory>();
        private Vector2? _pointer = null, _pressed = null, _released = null;
        private Pose3D _viewerPose = new Pose3D();
        private GraphicViewModel _viewModel { get; }
        private ObservableCollection<TopicStub> _allTopics { get; }
            = new ObservableCollection<TopicStub>();

        public GraphicView(GraphicViewModel model) {
            InitializeComponent();
            _viewModel = model ?? throw new ArgumentNullException(paramName: nameof(model));
            _viewModel.SetControl(MainCanvas);
        }

        private void CanvasControl_Draw(CanvasControl sender, CanvasDrawEventArgs args) {
            // 筛选活跃话题
            var actives = _viewModel.Topics.Where(it => it.Active).ToList();
            if (actives.None()) return;
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
            // 缓存数据
            if (!_viewModel.IsLocked)
                _memory = (_viewModel.Dim switch
                {
                    1 => from topic in actives
                         let data = ((TopicViewModel1)topic).Split()
                         where data.Any()
                         select (ITopicMemory)new TopicMemory1(topic, data),
                    2 => from topic in actives
                         let data = ((TopicViewModel2)topic).Split()
                         where data.Any()
                         select (ITopicMemory)new TopicMemory2(topic, data),
                    3 => from topic in actives
                         let data = ((TopicViewModel3)topic).Split()
                         where data.Any()
                         select (ITopicMemory)new TopicMemory3(topic, data),
                    _ => throw new InvalidDataException()
                }).ToList();
            // 转换到视平面
            var mapped = (_viewModel.Dim switch
            {
                1 => from topic in _memory
                     select ((TopicMemory1)topic)
                                .Process(ref areaX, ref areaY, ref areaXFrame, ref areaYFrame, topic.FrameMode),
                2 => from topic in _memory
                     select ((TopicMemory2)topic)
                                .Process(ref areaX, ref areaY, ref areaXFrame, ref areaYFrame, topic.FrameMode),
                3 => from topic in _memory
                     select ((TopicMemory3)topic)
                                .Process(ref areaX, ref areaY, ref areaXFrame, ref areaYFrame, topic.FrameMode, _viewerPose),
                _ => throw new InvalidDataException()
            }).ToList();
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
                _viewModel.Transform(width, height, BlankBorderWidth, out _, out var tf);
                var a = tf(_pressed.Value);
                var b = tf(_released.Value);
                _viewModel.CurrentRange(width, height, out var currentX, out var currentY);
                _viewModel.RangeX = Area.Auto(a.X, b.X).Determine(currentX, true);
                _viewModel.RangeY = Area.Auto(a.Y, b.Y).Determine(currentY, true);
                _pressed = _released = null;
            }
            // 根据范围计算变换
            _viewModel.Transform(width, height,
                                 BlankBorderWidth,
                                 out var transform,
                                 out var inverse);
            // 在缓存上迭代
            for (var i = 0; i < _memory.Count; ++i) {
                var (color, connect, r) = _memory[i];
                var topic = mapped[i];
                foreach (var group in topic) {
                    Vector2 current = default;
                    var notFirst = false;
                    foreach (var pose in group) {
                        // 更新缓存
                        var last = current;
                        current = transform(new Vector2(pose.X, pose.Y));
                        // 画点
                        brush.DrawCircle(current.X, current.Y, 1, color, r);
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
                brush.DrawText($"{p.X.ToString("0.000")}, {p.Y.ToString("0.000")}", x0 + 1, y0 - 15, Colors.Black, _textFormat);
                brush.DrawText($"{p.X.ToString("0.000")}, {p.Y.ToString("0.000")}", x0, y0 - 16, Colors.White, _textFormat);
                if (_pressed.HasValue) {
                    var x1 = _pressed.Value.X;
                    var y1 = _pressed.Value.Y;
                    brush.DrawLine(new Vector2(x0, y1 + 1), new Vector2(x1, y1 + 1), Colors.Black);
                    brush.DrawLine(new Vector2(x1 + 1, y0), new Vector2(x1 + 1, y1), Colors.Black);
                    brush.DrawLine(new Vector2(x0, y1), new Vector2(x1, y1), Colors.White);
                    brush.DrawLine(new Vector2(x1, y0), new Vector2(x1, y1), Colors.White);
                }
                foreach (var group in MainPage.Groups) {
                    var buffer = new byte[4 * sizeof(float)];
                    using var stream = new MemoryStream(buffer);
                    using var writer = new NetworkDataWriter(stream);
                    writer.Write((x0 - BlankBorderWidth) / width);
                    writer.Write((y0 - BlankBorderWidth) / height);
                    writer.Write(p.X);
                    writer.Write(p.Y);
                    group.Hub.Broadcast((byte)UdpCmd.Common, buffer);
                }
            }
        }
        private void TopicListSelectionChanged(object sender, SelectionChangedEventArgs e) {
            e.AddedItems
                .OfType<TopicStub>()
                .SingleOrDefault()
                ?.Let<TopicStub, TopicViewModelBase>(it => _viewModel.Dim switch {
                    1 => new TopicViewModel1(it, _viewModel),
                    2 => new TopicViewModel2(it, _viewModel),
                    3 => new TopicViewModel3(it, _viewModel),
                    _ => throw new IndexOutOfRangeException()
                })
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

    /// <summary>
    /// 一维区间
    /// </summary>
    public struct Area {
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
        public ObservableCollection<TopicViewModelBase> Topics { get; }
            = new ObservableCollection<TopicViewModelBase>();

        private CanvasControl _canvas;
        private Color _background = Colors.Transparent;
        private bool _locked = false,
                     _axisEquals,
                     _autoWidthAll = true,
                     _autoWidthFrame = false,
                     _allowWidthShrink = false,
                     _autoHeightAll = true,
                     _autoHeightFrame = false,
                     _allowHeightShrink = false,
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
        public bool Collapse1D => Dim != 1;

        internal void Transform(
            float width,
            float height,
            float borderWidth,
            out Func<Vector2, Vector2> transform,
            out Func<Vector2, Vector2> inverse
        ) {
            var o = new Vector2(borderWidth, borderWidth);
            var c0 = new Vector2(RangeX.C, RangeY.C);
            var c1 = new Vector2(width, height) / 2;
            var kx = width / RangeX.L;
            var ky = height / RangeY.L;
            Vector2 mirror(Vector2 it) => new Vector2(it.X, height - it.Y);
            if (AxisEquals) {
                var k = Math.Min(kx, ky);
                transform = p => mirror(k * (p - c0) + c1) + o;
                inverse = p => (mirror(p - o) - c1) / k + c0;
            } else {
                var k = new Vector2(kx, ky);
                transform = p => mirror(k * (p - c0) + c1) + o;
                inverse = p => (mirror(p - o) - c1) / k + c0;
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
    /// 话题模型的公共基类
    /// </summary>
    public abstract class TopicViewModelBase : BindableBase {
        private readonly string _remote;
        private Color _color = NewRandomColor();
        private bool _active = true,
                     _pause = false,
                     _background = false,
                     _connect = false;
        private float _radius = 2;

        protected abstract ITopicNode Core { get; }
        protected GraphicViewModel Graph { get; }
        protected TopicViewModelBase(string remote, GraphicViewModel graph) {
            _remote = remote;
            Graph = graph;
        }

        public string Title => $"{_remote}-{Core.Name}";
        public bool FrameMode => Core is FrameNodeBase;
        public Color Color {
            get => _color;
            set {
                if (SetProperty(ref _color, value))
                    Graph.Paint();
            }
        }
        public bool Active {
            get => _active;
            set {
                if (!SetProperty(ref _active, value)) return;
                Core.SetLevel(Graph, _active && !_pause ? TopicState.Active : TopicState.Subscribed);
                Graph.Paint();
            }
        }
        public bool IsPaused {
            get => _pause;
            set {
                if (!SetProperty(ref _pause, value)) return;
                Core.SetLevel(Graph, _active && !_pause ? TopicState.Active : TopicState.Subscribed);
                Graph.Paint();
            }
        }
        public bool Background {
            get => _background;
            set {
                if (SetProperty(ref _background, value))
                    Graph.Paint();
            }
        }
        public bool Connect {
            get => _connect;
            set {
                if (SetProperty(ref _connect, value))
                    Graph.Paint();
            }
        }
        public float Radius {
            get => _radius;
            set {
                if (SetProperty(ref _radius, value))
                    Graph.Paint();
            }
        }

        internal bool CheckEquals(TopicStub stub)
            => _remote == stub.Remote && Core.Name == stub.Core.Name;
        internal void Close()
            => Core.SetLevel(Graph, TopicState.None);

        public override string ToString() => Title;
        public override bool Equals(object obj)
            => this == obj || Title == (obj as TopicViewModelBase)?.Title;
        public override int GetHashCode()
            => Title.GetHashCode(StringComparison.Ordinal);

        /// <summary>
        /// 去除分隔符并分段
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="list">列表</param>
        /// <param name="splitter">分隔符</param>
        /// <returns>数据</returns>
        protected static IEnumerable<List<T>> SplitData<T>(
           IEnumerable<T> list,
           Func<T, bool> splitter) {
            List<T> group = null;
            foreach (var item in list) {
                if (splitter(item)) {
                    if (group != null) {
                        yield return group;
                        group = null;
                    }
                } else {
                    group ??= new List<T>();
                    group.Add(item);
                }
            }
            if (group != null) 
                yield return group;
        }

        #region 生成随机颜色
        private static readonly Random _engine = new Random();
        private static byte Raise(byte b, byte value)
            => (byte)(b + value % (256 - b));
        private static Color NewRandomColor() {
            var argb = new byte[4];
            _engine.NextBytes(argb);
            return Color.FromArgb(Raise(192, argb[0]),
                                  Raise(32, argb[1]),
                                  Raise(32, argb[2]),
                                  Raise(32, argb[3]));
        }
        #endregion
    }

    /// <summary>
    /// 一维话题模型
    /// </summary>
    public class TopicViewModel1 : TopicViewModelBase {
        private readonly Accumulator1 _core;
        public IReadOnlyList<Vector2> Data => _core.Data;
        protected override ITopicNode Core => _core;

        internal TopicViewModel1(TopicStub stub, GraphicViewModel graph)
            : base(stub.Remote, graph) {
            _core = (Accumulator1)stub.Core;
            _core.SetLevel(Graph, TopicState.Active);
        }

        internal List<List<Vector2>> Split() {
            lock (_core.Data) {
                return SplitData(_core.Data, it => float.IsNaN(it.Y)).ToList();
            }
        }
    }

    /// <summary>
    /// 二维话题模型
    /// </summary>
    public class TopicViewModel2 : TopicViewModelBase {
        private readonly ITopicNode<Vector3> _core;
        public IReadOnlyList<Vector3> Data => _core.Data;
        protected override ITopicNode Core => _core;

        internal TopicViewModel2(TopicStub stub, GraphicViewModel graph)
            : base(stub.Remote, graph) {
            _core = (ITopicNode<Vector3>)stub.Core;
            _core.SetLevel(Graph, TopicState.Active);
        }

        internal List<List<Vector3>> Split() {
            lock (_core.Data) {
                return SplitData(_core.Data, it => float.IsNaN(it.X)).ToList();
            }
        }
    }

    /// <summary>
    /// 三维话题模型
    /// </summary>
    public class TopicViewModel3 : TopicViewModelBase {
        private readonly ITopicNode<Vector3> _core;
        public IReadOnlyList<Vector3> Data => _core.Data;
        protected override ITopicNode Core => _core;

        internal TopicViewModel3(TopicStub stub, GraphicViewModel graph)
            : base(stub.Remote, graph) {
            _core = (ITopicNode<Vector3>)stub.Core;
            _core.SetLevel(Graph, TopicState.Active);
        }

        internal List<List<Vector3>> Split() {
            lock (_core.Data) {
                return SplitData(_core.Data, it => float.IsNaN(it.X)).ToList();
            }
        }
    }
}
