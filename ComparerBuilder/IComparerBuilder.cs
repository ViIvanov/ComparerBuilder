using System.Collections.Generic;

namespace ComparerBuilder
{
  public interface IComparerBuilder
  {
    IReadOnlyList<IComparerExpression> Expressions { get; }
  }
}
