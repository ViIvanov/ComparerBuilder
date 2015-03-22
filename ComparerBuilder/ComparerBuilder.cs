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

    public static BinaryExpression IsNull(Expression expression) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if

      if(expression.IsTypeReference()) {
        return ReferenceEqual(expression, Null);
      } else {
        return Equal(expression, Null);
      }//if
    }

    public static BinaryExpression IsNotNull(Expression expression) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if

      if(expression.IsTypeReference()) {
        return ReferenceNotEqual(expression, Null);
      } else {
        return NotEqual(expression, Null);
      }//if
    }

    public static Expression ToObject(Expression expression) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if

      if(expression.IsTypeReference()) {
        return expression;
      } else {
        return Convert(expression, typeof(object));
      }//if
    }

    public static bool IsTypeReference(this Expression expression) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if

      return IsTypeReference(expression.Type);
    }

    public static bool IsTypeNullable(this Expression expression) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if

      var type = expression.Type;
      return type.IsTypeReference() || type.IsValueType && Nullable.GetUnderlyingType(type) != null;
    }

    private static bool IsTypeReference(this Type type) {
      if(type == null) {
        throw new ArgumentNullException(nameof(type));
      }//if

      return type.IsClass || type.IsInterface;
    }
  }
}
