using System.Linq.Expressions;

namespace GBricks.Collections
{
  public interface IComparerExpression
  {
    Expression BuildEquals(ParameterExpression first, ParameterExpression second, LambdaExpression assert = null);
    Expression BuildGetHashCode(ParameterExpression parameter);
    Expression BuildCompare(ParameterExpression first, ParameterExpression second, LambdaExpression assert = null);
  }
}
