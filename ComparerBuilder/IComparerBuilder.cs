using System.Collections.Generic;

namespace GBricks.Collections
{
  public interface IComparerBuilder
  {
    IReadOnlyList<IComparerExpression> Expressions { get; }
  }
}
