using System;
using System.Linq.Expressions;

namespace GBricks.Collections
{
  using static ReplaceVisitor;

  internal sealed class NestedComparerExpression<T> : IComparerExpression
  {
    public NestedComparerExpression(LambdaExpression expression, ComparerBuilder<T> builder) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      } else if(builder == null) {
        throw new ArgumentNullException(nameof(builder));
      }//if
  
      Expression = expression;
      Builder = builder;
    }
  
    public LambdaExpression Expression { get; }
    public ComparerBuilder<T> Builder { get; }
  
    private IComparerBuilderInterception GetInterception(IComparerBuilderInterception value)
      => value == DefaultInterception.Instance && Builder.Interception != null ? Builder.Interception : value;
  
    public override string ToString() => Expression.ToString();
  
    #region IComparerExpression Members
  
    public Expression AsEquals(ParameterExpression x, ParameterExpression y, Type comparedType, IComparerBuilderInterception interception = null) {
      var expression = Builder.BuildEquals(ComparerBuilder<T>.X, ComparerBuilder<T>.Y, comparedType, GetInterception(interception));
      var first = ReplaceParameters(Expression, x);
      var second = ReplaceParameters(Expression, y);
      return ReplaceParameters(expression, first, second);
    }
  
    public Expression AsGetHashCode(ParameterExpression obj, Type comparedType, IComparerBuilderInterception interception = null) {
      var expression = Builder.BuildGetHashCode(ComparerBuilder<T>.Obj, comparedType, GetInterception(interception));
      var value = ReplaceParameters(Expression, obj);
      return ReplaceParameters(expression, value);
    }
  
    public Expression AsCompare(ParameterExpression x, ParameterExpression y, Type comparedType, IComparerBuilderInterception interception = null) {
      var expression = Builder.BuildCompare(ComparerBuilder<T>.X, ComparerBuilder<T>.Y, comparedType, GetInterception(interception));
      var first = ReplaceParameters(Expression, x);
      var second = ReplaceParameters(Expression, y);
      return ReplaceParameters(expression, first, second);
    }
  
    #endregion IComparerExpression Members
  }
}
