using System.Collections.Generic;
using System.Linq.Expressions;

namespace ComparerBuilder
{
  internal interface IComparerBuilder
  {
    IReadOnlyList<IComparerExpression> Expressions { get; }
  }
}
