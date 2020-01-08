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
        private ObservableCollection<TopicViewModel> _allTopics
            = new ObservableCollection<TopicViewModel>();

        public GraphView(GraphicViewModel model) {
            InitializeComponent();
            _viewModel = model;
            model.Control = MainCanvas;
        }
        private void CanvasControl_Draw(CanvasControl sender, CanvasDrawEventArgs args) {
        }
        private void DisplayListSelectionChanged(object sender, SelectionChangedEventArgs e) {
            var list = ((ListView)sender).SelectedItems.OfType<TopicViewModel>();
            var count = list.Count();
        }
        private void TopicListSelectionChanged(object sender, SelectionChangedEventArgs e) {
            e.AddedItems
                .OfType<TopicViewModel>()
                .SingleOrDefault()
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
                                      where _viewModel.Topics.None(it => it.CheckEquals(host, topic.Name))
                                      select new TopicViewModel(host, topic.Name))
                    _allTopics.Add(topic);
            }
        }
    }

    public class GraphicViewModel : BindableBase {
        public readonly ObservableCollection<TopicViewModel> Topics;

        private CanvasControl _control;
        private Color _background = Colors.Transparent;
        private bool _locked = false,
                     _axisEquals = true,
                     _autoRange = true,
                     _autoMove = false;

        public GraphicViewModel(string name, byte dim) {
            Name = name;
            Dim = dim;
            Topics = new ObservableCollection<TopicViewModel>();
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

        public static SolidColorBrush Brushify(Color color)
           => new SolidColorBrush(color);
    }

    public class TopicViewModel : BindableBase {
        private Color _color = NewRandomColor();
        private readonly string _remote, _name;

        public string Title => $"{_remote}-{_name}";
        public Color Color {
            get => _color;
            set => SetProperty(ref _color, value);
        }

        public TopicViewModel(string remote, string name) {
            _remote = remote;
            _name = name;
        }
        public bool CheckEquals(string remote, string name) =>
            _remote == remote && _name == name;
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
