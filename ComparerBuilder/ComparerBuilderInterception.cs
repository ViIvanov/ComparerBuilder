using System.Collections.Generic;
using System.Linq.Expressions;

namespace GBricks.Collections
{
  public abstract class ComparerBuilderInterception
  {
    public virtual bool InterceptEquals<T>(Expression expression, bool value, T x, T y, IEqualityComparer<T> comparer, SourceInfo sourceInfo) => value;
    public virtual int InterceptGetHashCode<T>(Expression expression, int value, T obj, IEqualityComparer<T> comparer, SourceInfo sourceInfo) => value;
    public virtual int InterceptCompare<T>(Expression expression, int value, T x, T y, IComparer<T> comparer, SourceInfo sourceInfo) => value;
  }
}
