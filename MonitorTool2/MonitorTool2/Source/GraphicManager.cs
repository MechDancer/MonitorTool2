using System.Collections.Generic;
using System.Linq;

namespace MonitorTool2.Source {
    /// <summary>
    /// 话题订阅状态
    /// </summary>
    public enum TopicState { None, Subscribed, Active }

    /// <summary>
    /// 图像管理器
    /// </summary>
    internal class GraphicManager {
        private readonly HashSet<GraphicViewModel>
            // 订阅数据，但暂时不需要显示的图像
            _observers = new HashSet<GraphicViewModel>(),
            // 显示数据的图像
            _subscribers = new HashSet<GraphicViewModel>();

        /// <summary>
        /// 当前状态
        /// </summary>
        public TopicState State => 
            _subscribers.Any()
                ? TopicState.Active
            : _observers.Any()
                ? TopicState.Subscribed

                : TopicState.None;

        public void Paint() {
            foreach (var graphic in _subscribers)
                graphic.Paint();
        }

        /// <summary>
        /// 调整级别
        /// </summary>
        /// <param name="graphic">图对象</param>
        /// <param name="level">目标级别</param>
        public void SetLevel(GraphicViewModel graphic, TopicState level) {
            switch (level) {
                case TopicState.None:
                    if (_observers.Remove(graphic))
                        _subscribers.Remove(graphic);
                    break;
                case TopicState.Subscribed:
                    if (!_subscribers.Remove(graphic))
                        _observers.Add(graphic);
                    break;
                case TopicState.Active:
                    if (_subscribers.Add(graphic))
                        _observers.Add(graphic);
                    break;
            }
        }
    }
}
