using System;
using System.Collections.Generic;

namespace GBricks.Collections
{
  internal static class Comparers
  {
    public static EqualityComparer<T> Create<T>(Func<T, T, bool> equals, Func<T, int> hashCode) => new MethodEqualityComparer<T>(equals, hashCode);
    public static Comparer<T> Create<T>(Func<T, T, int> compare) => new MethodComparer<T>(compare);

    public static int RotateRight(int value, int places) {
      if((places &= 0x1F) == 0) {
        return value;
      }//if

      var mask = ~0x7FFFFFFF >> (places - 1);
      return ((value >> places) & ~mask) | ((value << (32 - places)) & mask);
    }

    [Serializable]
    private sealed class MethodEqualityComparer<T> : EqualityComparer<T>
    {
      public MethodEqualityComparer(Func<T, T, bool> equals, Func<T, int> hashCode) {
        if(equals == null) {
          throw new ArgumentNullException(nameof(equals));
        } else if(hashCode == null) {
          throw new ArgumentNullException(nameof(hashCode));
        }//if

        EqualsMethod = equals;
        GetHashCodeMethod = hashCode;
      }

      private Func<T, T, bool> EqualsMethod { get; }
      private Func<T, int> GetHashCodeMethod { get; }

      public override bool Equals(T x, T y) => EqualsMethod(x, y);
      public override int GetHashCode(T obj) => GetHashCodeMethod(obj);

      public override bool Equals(object obj) {
        var other = obj as MethodEqualityComparer<T>;
        return other != null
            && other.EqualsMethod == EqualsMethod
            && other.GetHashCodeMethod == GetHashCodeMethod;
      }

      public override int GetHashCode() => EqualsMethod.GetHashCode() ^ GetHashCodeMethod.GetHashCode();
    }

    [Serializable]
    private sealed class MethodComparer<T> : Comparer<T>
    {
      public MethodComparer(Func<T, T, int> compare) {
        if(compare == null) {
          throw new ArgumentNullException(nameof(compare));
        }//if

        CompareMethod = compare;
      }

      private Func<T, T, int> CompareMethod { get; }

      public override int Compare(T x, T y) => CompareMethod(x, y);
      public override bool Equals(object obj) => (obj as MethodComparer<T>)?.CompareMethod == CompareMethod;
      public override int GetHashCode() => CompareMethod.GetHashCode();
    }
  }
}
