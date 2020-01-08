using MechDancer.Common;
using MechDancer.Framework.Net.Presets;
using MechDancer.Framework.Net.Protocol;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace MonitorTool2 {
    public sealed partial class MainPage {
        public static readonly ObservableCollection<GroupNode> Groups 
            = new ObservableCollection<GroupNode>();

        private GraphView _current;
        private IPEndPoint _memory;
        private readonly HashSet<IPEndPoint> _endPoints;
        private readonly ObservableCollection<GraphicViewModel> _graphs;
        public MainPage() {
            _endPoints = new HashSet<IPEndPoint>();
            _graphs = new ObservableCollection<GraphicViewModel>() {
                          new GraphicViewModel("默认", 2)
                      };

            InitializeComponent();
        }
        private void ShowTopics(object sender, RoutedEventArgs e) => ConfigView.IsPaneOpen = true;
        private void ShowGraphList(object sender, RoutedEventArgs e) => GraphList.IsPaneOpen = true;
        private void AddGroup(object sender, RoutedEventArgs e) {
            if (_endPoints.Contains(_memory)) return;
            _endPoints.Add(_memory);
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
            node.Hub.Yell();
            Groups.Remove(node);
        }
        private void ReSync_Click(object sender, RoutedEventArgs e) {
            if (!((sender as Button)?.DataContext is RemoteNode node)) return;
            node.Close();
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
        private void AddGraph(object sender, RoutedEventArgs e) {
            var name = GraphNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name) || _graphs.Any(it => it.Name == name)) return;
            _graphs.Add(new GraphicViewModel(name, (byte)(DimBox.SelectedIndex + 1)));
        }
        private void RemoveGraph(object sender, RoutedEventArgs e) {
            if (!((sender as Button)?.DataContext is GraphicViewModel graph)) return;
            _graphs.Remove(graph);
        }
        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (_current != null) MainGrid.Children.Remove(_current);
            _current = e.AddedItems
                .OfType<GraphicViewModel>()
                .SingleOrDefault()
                ?.Let(it => new GraphView(it))
                .Also(it => {
                    MainGrid.Children.Add(it);
                    Grid.SetColumn(it, 1);
                });
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
