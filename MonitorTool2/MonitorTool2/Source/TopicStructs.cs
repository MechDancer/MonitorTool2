using MechDancer.Common;
using MonitorTool2.Source;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Windows.UI;

namespace MonitorTool2 {
    /// <summary>
    /// 话题存根
    /// </summary>
    internal class TopicStub {
        public readonly string Remote;
        public readonly ITopicNode Core;

        public TopicStub(string remote, ITopicNode core) {
            Remote = remote;
            Core = core;
        }
        public override string ToString() => $"{Remote}-{Core.Name}";
    }

    /// <summary>
    /// 话题数据缓存
    /// </summary>
    internal interface ITopicMemory {
        public Color Color { get; }
        public bool Connect { get; }
        public float Radius { get; }
        public float Width { get; }
        public bool FrameMode { get; }

        public void Deconstruct(out Color color, out bool connect, out float radius, out float width);
    }

    /// <summary>
    /// 话题数据缓存基类
    /// </summary>
    /// <remarks>
    /// 包含显示数据所需的一切信息
    /// </remarks>
    /// <typeparam name="T">数据类型</typeparam>
    internal abstract class TopicMemoryBase<T> : ITopicMemory where T : struct {
        public Color Color { get; }
        public bool Connect { get; }
        public float Radius { get; }
        public float Width { get; }
        public bool FrameMode { get; }
        public List<List<T>> Data { get; }
        protected TopicMemoryBase(TopicViewModelBase topic, List<List<T>> data) {
            Color = topic.Color;
            Connect = topic.Connect;
            Radius = topic.Radius;
            Width = topic.Width;
            FrameMode = topic.FrameMode;
            Data = data;
        }
        public void Deconstruct(out Color color, out bool connect, out float radius, out float width) {
            color = Color;
            connect = Connect;
            radius = Radius;
            width = Width;
        }

        protected List<List<Vector3>> ProcessInternal(
            ref Area? xAll,
            ref Area? yAll,
            ref Area? xFrame,
            ref Area? yFrame,
            bool frameMode,
            Func<T, Vector3> block
        ) {
            var _xAll = xAll;
            var _yAll = yAll;
            var _xFrame = xFrame;
            var _yFrame = yFrame;

            void A(Vector3 it) {
                _xAll += it.X;
                _yAll += it.Y;
            }

            void B(Vector3 it) {
                _xFrame += it.X;
                _yFrame += it.Y;
            }

            void C(Vector3 it) {
                A(it);
                B(it);
            }

            var func = frameMode ? (Action<Vector3>)C : A;
            var result = Data
                .Select(group => group.Select(block).OnEach(func).ToList())
                .OnEach(group => { if (!frameMode) B(group.Last()); })
                .ToList();

            xAll = _xAll;
            yAll = _yAll;
            xFrame = _xFrame;
            yFrame = _yFrame;

            return result;
        }
    }

    internal class TopicMemory1 : TopicMemoryBase<Vector2> {
        public TopicMemory1(TopicViewModelBase topic, List<List<Vector2>> data)
            : base(topic, data) { }

        public List<List<Vector3>> Process(
            ref Area? xAll,
            ref Area? yAll,
            ref Area? xFrame,
            ref Area? yFrame,
            bool frameMode
        ) => ProcessInternal(
                ref xAll,
                ref yAll,
                ref xFrame,
                ref yFrame,
                frameMode,
                p => new Vector3(p.X, p.Y, float.NaN));
    }

    internal class TopicMemory2 : TopicMemoryBase<Vector3> {
        public TopicMemory2(TopicViewModelBase topic, List<List<Vector3>> data)
            : base(topic, data) { }

        public List<List<Vector3>> Process(
            ref Area? xAll,
            ref Area? yAll,
            ref Area? xFrame,
            ref Area? yFrame,
            bool frameMode
        ) => ProcessInternal(
                ref xAll,
                ref yAll,
                ref xFrame,
                ref yFrame,
                frameMode,
                p => new Vector3(p.X, p.Y, p.Z));
    }

    internal class TopicMemory3 : TopicMemoryBase<Vector3> {
        public TopicMemory3(TopicViewModelBase topic, List<List<Vector3>> data)
            : base(topic, data) { }

        public List<List<Vector3>> Process(
            ref Area? xAll,
            ref Area? yAll,
            ref Area? xFrame,
            ref Area? yFrame,
            bool frameMode,
            Pose3D pose
        ) => ProcessInternal(
                ref xAll,
                ref yAll,
                ref xFrame,
                ref yFrame,
                frameMode,
                p => (pose * p).Let(it => new Vector3(it.X, it.Y, float.NaN)));
    }

    internal static partial class Functions {
        public static IEnumerable<T> OnEach<T>(
            this IEnumerable<T> source,
            Action<T> block
        ) {
            if (source == null) yield break;
            foreach (var item in source) {
                block(item);
                yield return item;
            }
        }

        public static Vector2 Normalize(this Vector2 v) {
            var l = v.Length();
            return l < float.Epsilon ? Vector2.Zero : Vector2.Normalize(v);
        }

        public static Vector3 Normalize(this Vector3 v) {
            var l = v.Length();
            return l < float.Epsilon ? Vector3.Zero : Vector3.Normalize(v);
        }
    }
}
