using System;

namespace GBricks.Collections
{
  public struct SourceInfo : IEquatable<SourceInfo>, IComparable<SourceInfo>, IComparable
  {
    private readonly string filePath;

    public SourceInfo(string filePath, int lineNumber) {
      this.filePath = filePath;
      LineNumber = lineNumber;
    }

    public string FilePath => filePath ?? String.Empty;
    public int LineNumber { get; }

    public override bool Equals(object obj) => obj is SourceInfo && Equals((SourceInfo)obj);
    public override int GetHashCode() => FilePath.GetHashCode() ^ LineNumber;
    public override string ToString() => $"{FilePath} ({LineNumber})";

    #region IEquatable<SourceInfo> Members

    public bool Equals(SourceInfo other) {
      return other.FilePath == FilePath && other.LineNumber == LineNumber;
    }

    #endregion IEquatable<SourceInfo> Members

    #region IComparable Members

    int IComparable.CompareTo(object obj) {
      if(!(obj is SourceInfo)) {
        throw new ArgumentException("Argument is not the same type as this instance.", nameof(obj));
      }//if

      return CompareTo((SourceInfo)obj);
    }

    #endregion IComparable Members

    #region IComparable<SourceInfo> Members

    public int CompareTo(SourceInfo other) {
      var compare = FilePath.CompareTo(other.FilePath);
      if(compare == 0) {
        return LineNumber.CompareTo(other.LineNumber);
      }//if
      return compare;
    }

    #endregion IComparable<SourceInfo> Members

    public static bool operator ==(SourceInfo left, SourceInfo right) {
      return left.Equals(right);
    }

    public static bool operator !=(SourceInfo left, SourceInfo right) {
      return !(left == right);
    }

    public static bool operator <(SourceInfo left, SourceInfo right) {
      return left.CompareTo(right) < 0;
    }

    public static bool operator >(SourceInfo left, SourceInfo right) {
      return left.CompareTo(right) > 0;
    }

    public static bool operator <=(SourceInfo left, SourceInfo right) {
      return !(left > right);
    }

    public static bool operator >=(SourceInfo left, SourceInfo right) {
      return !(left < right);
    }
  }
}
