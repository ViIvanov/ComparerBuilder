using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;

namespace GBricks.Collections
{
  [DebuggerDisplay("{Expression} at {FilePath} ({LineNumber})")]
  public sealed class ComparerBuilderInterceptionArgs<T>
  {
    public ComparerBuilderInterceptionArgs(Expression expression, IEqualityComparer<T> equalityComparer, IComparer<T> comparisonComparer, string filePath, int lineNumber) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if
  
      Expression = expression;
      EqualityComparer = equalityComparer;
      ComparisonComparer = comparisonComparer;
      FilePath = filePath ?? String.Empty;
      LineNumber = lineNumber;
    }
  
    public Expression Expression { get; }
    public IEqualityComparer<T> EqualityComparer { get; }
    public IComparer<T> ComparisonComparer { get; }
    public string FilePath { get; }
    public int LineNumber { get; }
  }
}
