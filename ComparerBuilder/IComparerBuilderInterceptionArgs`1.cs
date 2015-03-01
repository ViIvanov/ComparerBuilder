using System.Collections.Generic;
using System.Linq.Expressions;

namespace GBricks.Collections
{
  public interface IComparerBuilderInterceptionArgs<T>
  {
    LambdaExpression Expression { get; }
    IEqualityComparer<T> EqualityComparer { get; }
    IComparer<T> Comparer { get; }
    string FilePath { get; }
    int LineNumber { get; }
  }
}
