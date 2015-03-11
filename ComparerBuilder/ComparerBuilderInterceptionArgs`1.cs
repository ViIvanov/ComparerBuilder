using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace GBricks.Collections
{
  public sealed class ComparerBuilderInterceptionArgs<T>
  {
    internal ComparerBuilderInterceptionArgs(LambdaExpression expression, Type comparedType, IEqualityComparer<T> equality, IComparer<T> comparison, string filePath, int lineNumber) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      } else if(comparedType == null) {
        throw new ArgumentNullException(nameof(comparedType));
      }//if

      Expression = expression;
      ComparedType = comparedType;
      EqualityComparer = equality;
      Comparer = comparison;
      FilePath = filePath ?? String.Empty;
      LineNumber = lineNumber;
    }

    public LambdaExpression Expression { get; }
    public Type ComparedType { get; }
    public IEqualityComparer<T> EqualityComparer { get; }
    public IComparer<T> Comparer { get; }
    public string FilePath { get; }
    public int LineNumber { get; }
  }
}
