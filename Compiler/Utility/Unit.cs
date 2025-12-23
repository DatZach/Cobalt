using System.Diagnostics.CodeAnalysis;

namespace Compiler.Utility
{
    public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>, IComparable
    {
        public static ref readonly Unit Value => ref value;

        private static readonly Unit value = new();

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is Unit;

        public bool Equals(Unit other) => true;

        public int CompareTo(Unit other) => 0;

        public int CompareTo(object? obj) => 0;

        public override int GetHashCode() => 0;

        public override string ToString() => "()";

        public static bool operator ==(Unit a, Unit b) => true;

        public static bool operator !=(Unit a, Unit b) => false;
    }
}
