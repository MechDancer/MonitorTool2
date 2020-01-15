using MechDancer.Common;
using MechDancer.Framework.Net.Presets;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace MonitorTool2 {
    public sealed partial class MainPage {
        private const string _endPointsKey = "endPoints";
        private static readonly ApplicationDataContainer
            _localSettings = ApplicationData.Current.LocalSettings;

        public static readonly ObservableCollection<GroupNode>
            Groups = new ObservableCollection<GroupNode>();

        private Tuple<GraphicView, GraphicViewModel> _current;

        private IPEndPoint _memory;

        private readonly HashSet<IPEndPoint> _endPoints;
        private readonly ObservableCollection<GraphicViewModel> _graphs;
        public MainPage() {
            _endPoints =
                ((string)_localSettings.Values[_endPointsKey])
                ?.Split('\n')
                 .SelectNotNull(it => TryParseIPEndPoint(it, out var ip) ? ip : null)
                 .Let(it => new HashSet<IPEndPoint>(it))
                ?? new HashSet<IPEndPoint>();
            foreach (var ip in _endPoints) {
                _memory = ip;
                AddGroup();
            }

            _graphs = new ObservableCollection<GraphicViewModel>() {
                          new GraphicViewModel("默认", 2)
                      };

            InitializeComponent();
        }
        private void ShowTopics(object sender, RoutedEventArgs e) => ConfigView.IsPaneOpen = true;
        private void ShowGraphList(object sender, RoutedEventArgs e) => GraphList.IsPaneOpen = true;
        private void AddGroup() {
            var newHub = new RemoteHub(name: $"Monitor[{_memory}]", group: _memory);
            var node = new GroupNode(newHub);

            Task.Run(() => {
                do {
                    var pack = newHub.Invoke();
                    if (pack != null) this.Dispatch(_ => node.Receive(pack));
                } while (_endPoints.Contains(_memory));
            });
            Task.Run(() => new Pacemaker(_memory).Activate());

            Groups.Add(node);
        }
        private void SaveGroups() {
            var builder = new StringBuilder();
            foreach (var ip in _endPoints)
                builder.AppendLine(ip.ToString());
            _localSettings.Values[_endPointsKey] = builder.ToString();
        }
        private void AddGroup_Click(object sender, RoutedEventArgs e) {
            if (_endPoints.Contains(_memory)) return;
            _endPoints.Add(_memory);
            Task.Run(() => SaveGroups());
            AddGroup();
        }
        private void TextBox_TextChanged(object sender, TextChangedEventArgs e) {
            AddButton.IsEnabled = TryParseIPEndPoint(((TextBox)sender).Text, out _memory);
        }
        private void Yell_Click(object sender, RoutedEventArgs e) {
            if (!((sender as Button)?.DataContext is GroupNode node)) return;
            node.Hub.Yell();
        }
        private void ShutDown_Click(object sender, RoutedEventArgs e) {
            if (!((sender as Button)?.DataContext is GroupNode node)) return;
            _endPoints.Remove(node.Address);
            Task.Run(() => SaveGroups());
            node.Hub.Yell();
            Groups.Remove(node);
        }
        private void ReSync_Click(object sender, RoutedEventArgs e) {
            if (!((sender as Button)?.DataContext is RemoteNode node)) return;
            node.Close();
        }
        private void ClearData_Click(object sender, RoutedEventArgs e) {
            if (!((sender as Button)?.DataContext is ITopicNode node)) return;
            node.Clear();
        }
        private void AddGraph(object sender, RoutedEventArgs e) {
            var name = GraphNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name) || _graphs.Any(it => it.Name == name)) return;
            _graphs.Add(new GraphicViewModel(name, (byte)(DimBox.SelectedIndex + 1)));
        }
        private void RemoveGraph(object sender, RoutedEventArgs e) {
            if (!((sender as Button)?.DataContext is GraphicViewModel graph)) return;
            _graphs.Remove(graph);
            graph.Close();
        }
        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (_current != null) {
                var (view, model) = _current;
                MainGrid.Children.Remove(view);
                model.Pause();
            }
            _current =
                e.AddedItems
                .OfType<GraphicViewModel>()
                .SingleOrDefault()
                ?.Let(model => {
                    if (model.Dim < 3) {
                        var view = new GraphicView(model);
                        model.Resume();
                        MainGrid.Children.Add(view);
                        Grid.SetColumn(view, 1);
                        return Tuple.Create(view, model);
                    } else return null;
                });
        }
        private void LeftInline(object sender, RoutedEventArgs e) {
            VisualStateManager.GoToState(this, nameof(LeftInlineState), false);
        }
        private void LeftOverlay(object sender, RoutedEventArgs e) {
            VisualStateManager.GoToState(this, nameof(LeftOverlayState), false);
            ConfigView.IsPaneOpen = false;
        }

        private static bool TryParseIPEndPoint(string text, out IPEndPoint result) {
            result = new IPEndPoint(new IPAddress(new byte[4]), 0);

            var temp = text.Split(':');
            if (temp.Length != 2) return false;
            if (!ushort.TryParse(temp[1], out var port)) return false;
            result.Port = port;

            var ip = new byte[4];
            temp = temp[0].Split('.');
            if (temp.Length != ip.Length) return false;
            for (var i = 0; i < ip.Length; ++i)
                if (!byte.TryParse(temp[i], out ip[i])) return false;
            result.Address = new IPAddress(ip);
            return true;
        }
    }

    /// <summary>
    /// 模板选择器
    /// </summary>
    public class TreeTemplateSelector : DataTemplateSelector {
        public DataTemplate GroupTemplate { get; set; }
        public DataTemplate RemoteTemplate { get; set; }
        public DataTemplate DimensionTemplate { get; set; }
        public DataTemplate FrameTemplate { get; set; }
        public DataTemplate AccumulatorTemplate { get; set; }
        protected override DataTemplate SelectTemplateCore(object item) {
            if (item is GroupNode) return GroupTemplate;
            if (item is RemoteNode) return RemoteTemplate;
            if (item is DimensionNodeBase) return DimensionTemplate;
            if (item is FrameNodeBase) return FrameTemplate;
            if (item is AccumulatorNodeBase) return AccumulatorTemplate;
            throw new MissingMemberException();
        }
    }

    /// <summary>
    /// 扩展函数
    /// </summary>
    internal static class Functions {
        internal static void Dispatch<T>(this T control, Action<T> action)
          where T : DependencyObject
          => control.Dispatcher.RunAsync(
              CoreDispatcherPriority.Normal,
              () => action(control));
    }
}
