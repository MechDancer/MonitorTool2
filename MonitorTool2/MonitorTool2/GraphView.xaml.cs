using MechDancer.Common;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.UI;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace MonitorTool2 {
    public sealed partial class GraphView {
        private GraphicViewModel _viewModel { get; }
        private ObservableCollection<TopicStub> _allTopics
            = new ObservableCollection<TopicStub>();

        public GraphView(GraphicViewModel model) {
            InitializeComponent();
            _viewModel = model;
            model.Control = MainCanvas;
        }

        public void Open() {
            foreach (var topic in _viewModel.Topics) topic.IsPaused = false;
        }
        public void Close() {
            foreach (var topic in _viewModel.Topics) topic.IsPaused = true;
        }

        private void CanvasControl_Draw(CanvasControl sender, CanvasDrawEventArgs args) {
        }
        private void TopicListSelectionChanged(object sender, SelectionChangedEventArgs e) {
            e.AddedItems
                .OfType<TopicStub>()
                .SingleOrDefault()
                ?.Let(it => new TopicViewModel(it))
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

    public class GraphicViewModel : BindableBase {
        public readonly ObservableCollection<TopicViewModel> Topics
            = new ObservableCollection<TopicViewModel>();

        private CanvasControl _control;
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
                if (Control != null) Control.ClearColor = value;
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
        public CanvasControl Control {
            get => _control;
            set {
                if (_control == value) return;
                _control = value;

                if (_control == null) return;
                _control.ClearColor = _background;
            }
        }
        public void Close() {
            _control = null;
            foreach (var topic in Topics) topic.Close();
        }

        public static SolidColorBrush Brushify(Color color)
           => new SolidColorBrush(color);
    }

    public class TopicStub {
        public readonly string Remote;
        public readonly ITopicNode Core;

        public TopicStub(string remote, ITopicNode core) {
            Remote = remote;
            Core = core;
        }
        public override string ToString() => $"{Remote}-{Core.Name}";
    }

    public class TopicViewModel : BindableBase {
        private readonly string _remote;
        private readonly ITopicNode _core;
        private Color _color = NewRandomColor();
        private bool _active = false, 
                     _pause = false;

        public string Title => $"{_remote}-{_core.Name}";
        public Color Color {
            get => _color;
            set => SetProperty(ref _color, value);
        }
        public bool Active {
            get => _active;
            set {
                if (SetProperty(ref _active, value))
                    _core.SetLevel(this, _active && !_pause ? TopicState.Active : TopicState.Subscribed);
            }
        }
        public bool IsPaused {
            get => _pause;
            set {
                _pause = value;
                _core.SetLevel(this, _active && !_pause ? TopicState.Active : TopicState.Subscribed);
            }
        }

        public TopicViewModel(TopicStub stub) {
            _remote = stub.Remote;
            _core = stub.Core;
            _core.SetLevel(this, TopicState.Subscribed);
        }
        public bool CheckEquals(TopicStub stub) =>
            _remote == stub.Remote && _core.Name == stub.Core.Name;
        public void Close() {
            _core.SetLevel(this, TopicState.None);
        }

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
