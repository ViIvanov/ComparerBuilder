using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace GBricks.Collections
{
  using static ComparerBuilder;
  using static Expression;
  using static ReplaceVisitor;

  internal sealed class ComparerExpression<T> : IComparerExpression
  {
    private static readonly MethodInfo GetHashCodeMethodInfo = typeof(T).GetMethod(nameof(GetHashCode), BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
    private static readonly Type[] InterceptTypeArguments = { typeof(T), };

    public ComparerExpression(LambdaExpression expression, Expression equality = null, Expression comparison = null, SourceInfo sourceInfo = default(SourceInfo)) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if

      Expression = expression;
      EqualityComparer = equality;
      Comparer = comparison;
      SourceInfo = sourceInfo;
    }

    public LambdaExpression Expression { get; }
    public Expression EqualityComparer { get; }
    public Expression Comparer { get; }
    public SourceInfo SourceInfo { get; }

    #region Comparison Helpers

    private static Expression MakeEquals(Expression x, Expression y, Expression comparer) {
      if(x == null) {
        throw new ArgumentNullException(nameof(x));
      } else if(y == null) {
        throw new ArgumentNullException(nameof(y));
      }//if

      if(comparer != null) {
        // comparer.Equals(x, y);
        return CallComparerMethod(comparer, nameof(IEqualityComparer<T>.Equals), x, y);
      } else {
        if(x.Type.IsValueType && y.Type.IsValueType) {
          // x == y;
          return Equal(x, y);
        } else {
          // Object.Equals(x, y);
          return Call(ObjectEqualsMethodInfo, ToObject(x), ToObject(y));
        }//if
      }//if
    }

    private static Expression MakeGetHashCode(Expression obj, Expression comparer) {
      if(obj == null) {
        throw new ArgumentNullException(nameof(obj));
      }//if

      if(comparer != null) {
        // comparer.GetHashCode(obj);
        return CallComparerMethod(comparer, nameof(IEqualityComparer<T>.GetHashCode), obj);
      } else {
        var method = obj.Type.IsValueType ? GetHashCodeMethodInfo : ObjectGetHashCodeMethodInfo;
        var call = Call(obj, method);
        if(obj.Type.IsValueType) {
          // obj.GetHashCode();
          return call;
        } else {
          // obj != null ? obj.GetHashCode() : 0
          return Condition(IsNotNull(obj), call, Zero);
        }//if
      }//if
    }

    private static Expression MakeCompare(Expression x, Expression y, Expression comparer) {
      if(x == null) {
        throw new ArgumentNullException(nameof(x));
      } else if(y == null) {
        throw new ArgumentNullException(nameof(y));
      }//if

      if(comparer != null) {
        // comparer.Compare(x, y);
        return CallComparerMethod(comparer, nameof(IComparer<T>.Compare), x, y);
      } else {
        // (x < y) ? -1 : (y < x ? 1 : 0);
        return Condition(LessThan(x, y), MinusOne, Condition(LessThan(y, x), One, Zero));
      }//if
    }

    private static Expression CallComparerMethod(Expression comparer, string methodName, params Expression[] arguments) {
      if(comparer == null) {
        throw new ArgumentNullException(nameof(comparer));
      }//if

      const BindingFlags MethodLookup = BindingFlags.Public | BindingFlags.Instance;
      var types = arguments != null && arguments.Any() ? Array.ConvertAll(arguments, item => item.Type) : Type.EmptyTypes;
      var method = comparer.Type.GetMethod(methodName, MethodLookup, null, types, null);
      if(method == null) {
        var message = $"Method \"{methodName}\" is not found in type \"{comparer.Type}\".";
        throw new ArgumentException(message, nameof(methodName));
      }//if

      return Call(comparer, method, arguments);
    }

    #endregion Comparison Helpers

    private Expression ApplyInterception(ComparerBuilderInterception interception, string methodName,
      Expression first, Expression second, Func<Expression, Expression, Expression, Expression> make,
      bool comparison) {
      var comparer = !comparison ? EqualityComparer : Comparer;
      var comparerType = !comparison ? typeof(IEqualityComparer<T>) : typeof(IComparer<T>);
      return ApplyInterception(interception, methodName, Expression, first, second, comparer, comparerType, SourceInfo, make);
    }

    private static Expression ApplyInterception(ComparerBuilderInterception interception, string methodName, Expression expression,
      Expression first, Expression second, Expression comparer, Type comparerType, SourceInfo sourceInfo,
      Func<Expression, Expression, Expression, Expression> make) {
      if(interception == null) {
        throw new ArgumentNullException(nameof(interception));
      } else if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      } else if(first == null) {
        throw new ArgumentNullException(nameof(first));
      } else if(make == null) {
        throw new ArgumentNullException(nameof(make));
      }//if

      // return {interception}.{methodName}<{valueType}>(expression, {expression}, {x}[, {y}], {comparer});

      var instance = Constant(interception);
      var expressionArg = Constant(expression, typeof(Expression));

      var firstArg = Parameter(first.Type);
      var assignFirst = Assign(firstArg, first);
      var useSecond = second != null;
      var secondArg = useSecond ? Parameter(second.Type) : null;
      var assignSecond = useSecond ? Assign(secondArg, second) : null;
      var variables = useSecond ? new[] { firstArg, secondArg, } : new[] { firstArg, };

      var valueArg = make(firstArg, secondArg, comparer);
      var comparerArg = comparer ?? Constant(null, comparerType);
      var sourceInfoArg = Constant(sourceInfo);
      var arguments = secondArg != null
        ? new[] { expressionArg, valueArg, firstArg, secondArg, comparerArg, sourceInfoArg, }
        : new[] { expressionArg, valueArg, firstArg, comparerArg, sourceInfoArg, };
      var call = Call(instance, methodName, InterceptTypeArguments, arguments);
      var expressions = useSecond
        ? new Expression[] { assignFirst, assignSecond, call, }
        : new Expression[] { assignFirst, call, };
      return Block(valueArg.Type, variables, expressions);
    }

    public override string ToString() => Expression.ToString();

    #region IComparerExpression Members

    public Expression AsEquals(ParameterExpression x, ParameterExpression y, ComparerBuilderInterception interception = null) {
      var first = ReplaceParameters(Expression, x);
      var second = ReplaceParameters(Expression, y);
      if(interception == null) {
        return MakeEquals(first, second, EqualityComparer);
      } else {
        const string MethodName = nameof(ComparerBuilderInterception.InterceptEquals);
        var comparerType = typeof(IEqualityComparer<T>);
        return ApplyInterception(interception, MethodName, first, second, (a, b, c) => MakeEquals(a, b, c), comparison: false);
      }//if
    }

    public Expression AsGetHashCode(ParameterExpression obj, ComparerBuilderInterception interception = null) {
      var value = ReplaceParameters(Expression, obj);
      if(interception == null) {
        return MakeGetHashCode(value, EqualityComparer);
      } else {
        const string MethodName = nameof(ComparerBuilderInterception.InterceptGetHashCode);
        var comparerType = typeof(IEqualityComparer<T>);
        return ApplyInterception(interception, MethodName, value, null, (a, b, c) => MakeGetHashCode(a, c), comparison: false);
      }//if
    }

    public Expression AsCompare(ParameterExpression x, ParameterExpression y, ComparerBuilderInterception interception = null) {
      var first = ReplaceParameters(Expression, x);
      var second = ReplaceParameters(Expression, y);
      if(interception == null) {
        return MakeCompare(first, second, Comparer);
      } else {
        const string MethodName = nameof(ComparerBuilderInterception.InterceptCompare);
        var comparerType = typeof(IComparer<T>);
        return ApplyInterception(interception, MethodName, first, second, (a, b, c) => MakeCompare(a, b, c), comparison: true);
      }//if
    }

    #endregion IComparerExpression Members
  }
}
