using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Windows.UI;
using Windows.UI.Xaml.Media;

namespace MonitorTool2 {
    public sealed partial class GraphView {
        private GraphicViewModel _viewModel { get; }

        public GraphView(GraphicViewModel model) {
            InitializeComponent();
            _viewModel = model;
            model.Control = MainCanvas;
        }
        private void CanvasControl_Draw(CanvasControl sender, CanvasDrawEventArgs args) {
        }
    }

    public class GraphicViewModel : BindableBase {
        public readonly ObservableCollection<TopicViewModel> Topics;

        private CanvasControl _control;
        private Color _background = Colors.Transparent;

        public GraphicViewModel(string name, byte dim) {
            Name = name;
            Dim = dim;
            Topics = new ObservableCollection<TopicViewModel>() {
                new TopicViewModel("测试话题")
            };
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

        public string Name { get; }
        public Color Color {
            get => _color;
            set => SetProperty(ref _color, value);
        }

        public TopicViewModel(string name) {
            Name = name;
        }

        private static readonly Random _engine = new Random();
        private static Color NewRandomColor() {
            var rgb = new byte[3];
             _engine.NextBytes(rgb);
            return Color.FromArgb(255, rgb[0], rgb[1], rgb[2]);
        }
    }
}
