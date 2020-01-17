using System;
using System.Diagnostics;
using System.Numerics;

namespace MonitorTool2.Source {
    /// <summary>
    /// 三维位姿
    /// </summary>
    public struct Pose : IEquatable<Pose> {
        public Vector3 P { get; }
        public Vector3 D { get; }

        public Pose(Vector3 p, Vector3 d) {
            P = p;
            D = d;
        }

        public Pose AddDelta(Pose delta) {
            ToQuaernions(out var v0, out var q0);
            delta.ToQuaernions(out var v1, out var q1);
            return new Pose(v0 + q0 * v1 * q0.Conjugate, q1 * q0);
        }
        public Pose SubtractDelta(Pose delta) {
            ToQuaernions(out var v0, out var q0);
            delta.ToQuaernions(out var v1, out var q1);
            var qi = q1.Conjugate * q0;
            return new Pose(v0 - qi * v1 * qi.Conjugate, qi);
        }
        public Pose SubtractState(Pose mark) {
            mark.ToQuaernions(out var v0, out var q0);
            ToQuaernions(out var v1, out var q1);
            return new Pose(q0.Conjugate * (v1 - v0) * q0, q1 * q0.Conjugate);
        }

        public static bool operator ==(Pose a, Pose b)
            => a.Equals(b);
        public static bool operator !=(Pose a, Pose b)
            => !a.Equals(b);

        public bool Equals(Pose other)
            => P == other.P && D == other.D;
        public override bool Equals(object obj)
            => obj is Pose pose && Equals(pose);
        public override int GetHashCode()
            => P.GetHashCode() ^ D.GetHashCode();

        public override string ToString() {
            var half = D.Length();
            if (MathF.Abs(half) < float.Epsilon)
                return $"P = {View(P)}, D = {View(default)}, θ = 0 rad";
            else
                return $"P = {View(P)}, D = {View(D / half)}, θ = {2 * half} rad";
        }

        private void ToQuaernions(out Quaternion p, out Quaternion d) {
            p = new Quaternion(0, P);
            var half = D.Length();
            d = new Quaternion(MathF.Cos(half),
                               MathF.Abs(half) < float.Epsilon
                               ? default
                               : D / half * MathF.Sin(half));
        }

        private Pose(Quaternion p, Quaternion d) {
            Debug.Assert(MathF.Abs(p.R) < float.Epsilon);
            P = p.V;
            var half = d.V.Length();
            D = MathF.Abs(half) < float.Epsilon ? default : d.V / half * MathF.Atan2(half, d.R);
        }

        private static string View(Vector3 v) =>
            $"({v.X}, {v.Y}, {v.Z})";
    }
}