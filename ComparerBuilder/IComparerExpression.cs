using System.Linq.Expressions;

namespace GBricks.Collections
{
  internal interface IComparerExpression
  {
    Expression AsEquals(ParameterExpression x, ParameterExpression y, IComparerBuilderInterception interception = null);
    Expression AsGetHashCode(ParameterExpression obj, IComparerBuilderInterception interception = null);
    Expression AsCompare(ParameterExpression x, ParameterExpression y, IComparerBuilderInterception interception = null);
  }
}
