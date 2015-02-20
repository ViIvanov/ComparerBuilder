using System.Linq.Expressions;

namespace ComparerBuilder
{
  internal interface IComparerExpression
  {
    LambdaExpression Expression { get; }
  
    Expression BuildEquals(ParameterExpression first, ParameterExpression second, LambdaExpression assert = null);
    Expression BuildGetHashCode(ParameterExpression parameter);
    Expression BuildCompare(ParameterExpression first, ParameterExpression second, LambdaExpression assert = null);
  }
}
