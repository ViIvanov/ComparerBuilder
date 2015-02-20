using System;
using System.Collections.Generic;

namespace ComparerBuilder
{
  internal static class Comparers
  {
    public static EqualityComparer<T> EmptyEqualityComparer<T>() => ConstEqualityComparer<T>.Default;
    public static Comparer<T> EmptyComparer<T>() => ConstComparer<T>.Default;
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

    [Serializable]
    private sealed class ConstEqualityComparer<T> : EqualityComparer<T>
    {
      public ConstEqualityComparer(bool equals, int hashCode) {
        EqualsValue = equals;
        GetHashCodeValue = hashCode;
      }

      public static new EqualityComparer<T> Default { get; } = new ConstEqualityComparer<T>(true, 0);

      private bool EqualsValue { get; }
      private int GetHashCodeValue { get; }

      public override bool Equals(T x, T y) => EqualsValue;
      public override int GetHashCode(T obj) => GetHashCodeValue;

      public override bool Equals(object obj) {
        var other = obj as ConstEqualityComparer<T>;
        return other != null
            && other.EqualsValue == EqualsValue
            && other.GetHashCodeValue == GetHashCodeValue;
      }

      public override int GetHashCode() => RotateRight(EqualsValue.GetHashCode(), 1) ^ RotateRight(GetHashCodeValue.GetHashCode(), 2);

      public override string ToString() => $"Equals = {EqualsValue}, HashCode = {GetHashCodeValue}";
    }

    [Serializable]
    private sealed class ConstComparer<T> : Comparer<T>
    {
      public ConstComparer(int compare) {
        CompareValue = compare;
      }

      public static new Comparer<T> Default { get; } = new ConstComparer<T>(0);

      private int CompareValue { get; }

      public override int Compare(T x, T y) => CompareValue;
      public override bool Equals(object obj) => (obj as ConstComparer<T>)?.CompareValue == CompareValue;
      public override int GetHashCode() => CompareValue;
      public override string ToString() => $"Value = {CompareValue}";
    }
  }
}
