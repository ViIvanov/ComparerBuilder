using System.Collections.Generic;

namespace GBricks.Collections
{
  public interface IComparerBuilder<in T>
  {
    IEnumerable<IComparerExpression> GetExpressions();
  }
}
