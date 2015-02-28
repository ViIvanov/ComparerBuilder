using System.Linq.Expressions;

namespace GBricks.Collections
{
  internal interface IComparerExpression
  {
    Expression AsEquals(ParameterExpression x, ParameterExpression y, ComparerBuilderInterception interception = null);
    Expression AsGetHashCode(ParameterExpression obj, ComparerBuilderInterception interception = null);
    Expression AsCompare(ParameterExpression x, ParameterExpression y, ComparerBuilderInterception interception = null);
  }
}
