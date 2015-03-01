using System;
using System.Linq.Expressions;
using System.Reflection;

namespace GBricks.Collections
{
  using static Expression;

  internal static class ComparerBuilder
  {
    public static ConstantExpression Null { get; } = Constant(null);
    public static ConstantExpression Zero { get; } = Constant(0);
    public static ConstantExpression False { get; } = Constant(false);

    public static ConstantExpression One { get; } = Constant(1);
    public static ConstantExpression MinusOne { get; } = Constant(-1);

    public static MethodInfo ObjectEqualsMethodInfo { get; } = new Func<object, object, bool>(Equals).Method;
    public static MethodInfo ObjectGetHashCodeMethodInfo { get; } = new Func<int>(new object().GetHashCode).Method;

    public static BinaryExpression IsNull(Expression value) => ReferenceEqual(value, Null);
    public static BinaryExpression IsNotNull(Expression value) => ReferenceNotEqual(value, Null);

    public static Expression ToObject(Expression expression) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if

      var type = expression.Type;
      if(type.IsClass || type.IsInterface) {
        return expression;
      } else {
        return Convert(expression, typeof(object));
      }//if
    }
  }
}
