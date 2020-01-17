using System;
using System.Numerics;

namespace MonitorTool2.Source {
    /// <summary>
    /// 四元数
    /// </summary>
    public struct Quaternion : IEquatable<Quaternion> {
        private readonly Vector4 _value;

        public Quaternion(Vector4 value) {
            _value = value;
        }
        public Quaternion(float a, float b, float c, float d)
            : this(new Vector4(a, b, c, d)) { }
        public Quaternion(float r, Vector3 v)
            : this(new Vector4(r, v.X, v.Y, v.Z)) { }

        public float R => _value.X;
        public Vector3 V => new Vector3(_value.Y, _value.Z, _value.W);
        public float Square => _value.LengthSquared();
        public float Length => _value.Length();
        public Quaternion Conjugate => new Quaternion(R, -V);
        public Quaternion Inverse => new Quaternion();

        public Quaternion Negate() => new Quaternion(-_value);
        public Quaternion Add(Quaternion others) => new Quaternion(_value + others._value);
        public Quaternion Subtract(Quaternion others) => new Quaternion(_value - others._value);
        public Quaternion Multiply(float k) => new Quaternion(_value * k);
        public Quaternion Divide(float k) => new Quaternion(_value / k);
        public Quaternion Multiply(Quaternion others) {
            var (a, b, c, d) = this;
            var (e, f, g, h) = others;
            return new Quaternion(
                a * e - b * f - c * g - d * h,
                b * e + a * f - d * g + c * h,
                c * e + d * f + a * g - b * h,
                d * e - c * f + b * g + a * h);
        }

        public void Deconstruct(out float a, out float b, out float c, out float d) {
            a = _value.X;
            b = _value.Y;
            c = _value.Z;
            d = _value.W;
        }

        public static Quaternion operator -(Quaternion q)
            => q.Negate();
        public static Quaternion operator +(Quaternion a, Quaternion b)
            => a.Add(b);
        public static Quaternion operator -(Quaternion a, Quaternion b)
            => a.Subtract(b);
        public static Quaternion operator *(Quaternion q, float k)
            => q.Multiply(k);
        public static Quaternion operator /(Quaternion q, float k)
            => q.Divide(k);
        public static Quaternion operator *(float k, Quaternion q)
            => q.Multiply(k);
        public static Quaternion operator /(float k, Quaternion q)
            => q.Divide(k);
        public static Quaternion operator *(Quaternion a, Quaternion b)
            => a.Multiply(b);
        public static bool operator ==(Quaternion a, Quaternion b)
            => a.Equals(b);
        public static bool operator !=(Quaternion a, Quaternion b)
            => !a.Equals(b);

        public bool Equals(Quaternion other)
            => _value == other._value;
        public override bool Equals(object obj)
            => obj is Quaternion q && Equals(q);
        public override int GetHashCode()
            => _value.GetHashCode();
    }
}
