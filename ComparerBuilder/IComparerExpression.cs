using System.Linq.Expressions;

namespace GBricks.Collections
{
  internal interface IComparerExpression
  {
    Expression AsEquals(ParameterExpression x, ParameterExpression y, LambdaExpression assert = null);
    Expression AsGetHashCode(ParameterExpression obj);
    Expression AsCompare(ParameterExpression x, ParameterExpression y, LambdaExpression assert = null);
  }
}
