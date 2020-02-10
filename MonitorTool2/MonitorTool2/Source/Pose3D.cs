using System;
using System.Diagnostics;
using System.Numerics;

namespace MonitorTool2.Source {
    /// <summary>
    /// 三维位姿
    /// </summary>
    public struct Pose3D : IEquatable<Pose3D> {
        /// <summary>
        /// 位置
        /// </summary>
        public Vector3 P { get; }

        /// <summary>
        /// 方向（压缩轴角表示）
        /// </summary>
        public Vector3 D { get; }

        /// <summary>
        /// 旋转轴
        /// </summary>
        public Vector3 U => Normalize(D);

        /// <summary>
        /// 转角
        /// </summary>
        public float Theta => 2 * D.Length();

        /// <summary>
        /// 主构造器
        /// </summary>
        /// <param name="p">位置</param>
        /// <param name="d">方向（压缩轴角表示）</param>
        public Pose3D(Vector3 p, Vector3 d) {
            P = p;
            D = d;
        }

        /// <summary>
        /// 变换一个位置
        /// </summary>
        /// <param name="pose">子坐标系姿态</param>
        /// <param name="p">位置在子坐标系上的坐标</param>
        /// <returns>位置在O坐标系上的坐标</returns>
        public static Vector3 Multiply(Pose3D pose, Vector3 p)
            => pose.P + RotateVector(p, pose.D);

        /// <summary>
        /// 变换一个姿态
        /// </summary>
        /// <param name="pose">子坐标系姿态</param>
        /// <param name="tf">子坐标系上的一个坐标系姿态</param>
        /// <returns>tf在O坐标系上的姿态</returns>
        public static Pose3D Multiply(Pose3D pose, Pose3D tf)
            => new Pose3D(pose.P + RotateVector(tf.P, pose.D),
                          RotateAngle(pose.D, tf.D));

        /// <summary>
        /// 反转
        /// </summary>
        /// <returns>作为反变换的位姿</returns>
        public Pose3D Inverse()
            => new Pose3D(RotateVector(-P, -D), -D);

        public static Vector3 operator *(Pose3D pose, Vector3 p)
            => Multiply(pose, p);
        public static Pose3D operator *(Pose3D pose, Pose3D tf)
            => Multiply(pose, tf);

        public Pose3D AddDelta(Pose3D delta)
            => this * delta;
        public Pose3D SubtractDelta(Pose3D delta)
            => this * delta.Inverse();
        public Pose3D SubtractState(Pose3D origin)
            => origin.Inverse() * this;

        public static bool operator ==(Pose3D a, Pose3D b)
            => a.Equals(b);
        public static bool operator !=(Pose3D a, Pose3D b)
            => !a.Equals(b);

        public bool Equals(Pose3D other)
            => P == other.P && D == other.D;
        public override bool Equals(object obj)
            => obj is Pose3D pose && Equals(pose);
        public override int GetHashCode()
            => P.GetHashCode() ^ D.GetHashCode();

        public override string ToString()
            => $"P = {View(P)}, D = {View(U)}, θ = {Theta} rad";

        private static string View(Vector3 v) =>
            $"({v.X}, {v.Y}, {v.Z})";
        private static Vector3 Normalize(Vector3 v) {
            var l = v.Length();
            return l < float.Epsilon ? new Vector3() : Vector3.Normalize(v);
        }
        private static Quaternion Position(Vector3 v) =>
           new Quaternion(0, v);
        private static Quaternion Angle(Vector3 v) {
            var half = v.Length();
            if (half > MathF.PI)
                half -= MathF.PI * (int)((half - MathF.PI) / MathF.PI);
            return new Quaternion(MathF.Cos(half), Normalize(v) * MathF.Sin(half));
        }
        private static Vector3 RotateVector(Vector3 v, Vector3 d) {
            var q = Angle(d);
            return (q * Position(v) * q.Conjugate).V;
        }
        private static Vector3 RotateAngle(Vector3 a, Vector3 d) {
            var q = Angle(a) * Angle(d);
            return Normalize(q.V) * MathF.Atan2(q.V.Length(), q.R);
        }
    }
}