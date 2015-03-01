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

  internal sealed class ComparerExpression<TProperty> : IComparerExpression
  {
    private static readonly Func<object, object, bool> ObjectEqualsDelegate = Equals;
    private static readonly Func<int> GetHashCodeDelegate = new object().GetHashCode;
    private static readonly Type[] InterceptTypeArguments = { typeof(TProperty), };
  
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
        return CallComparerMethod(comparer, nameof(IEqualityComparer<int>.Equals), x, y);
      } else {
        if(x.Type.IsValueType && y.Type.IsValueType) {
          // x == y;
          return Equal(x, y);
        } else {
          // Object.Equals(x, y);
          return Call(ObjectEqualsDelegate.Method, ToObject(x), ToObject(y));
        }//if
      }//if
    }
  
    private static Expression MakeGetHashCode(Expression obj, Expression comparer) {
      if(obj == null) {
        throw new ArgumentNullException(nameof(obj));
      }//if
  
      if(comparer != null) {
        // comparer.GetHashCode(obj);
        return CallComparerMethod(comparer, nameof(IEqualityComparer<int>.GetHashCode), obj);
      } else {
        var call = Call(obj, GetHashCodeDelegate.Method);
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
        return CallComparerMethod(comparer, nameof(IComparer<int>.Compare), x, y);
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
  
    #region Interception
  
    private static Expression InterceptEquals(ComparerBuilderInterception interception, Expression expression, Expression x, Expression y, Expression comparer, SourceInfo sourceInfo) {
      if(interception == null) {
        return expression;
      } else {
        const string MethodName = nameof(ComparerBuilderInterception.InterceptEquals);
        var comparerType = typeof(IEqualityComparer<TProperty>);
        return ApplyInterception(interception, MethodName, expression, x, y, comparer, comparerType, sourceInfo, (a, b, c) => MakeEquals(a, b, c));
      }//if
    }
  
    private static Expression InterceptGetHashCode(ComparerBuilderInterception interception, Expression expression, Expression obj, Expression comparer, SourceInfo sourceInfo) {
      if(interception == null) {
        return expression;
      } else {
        const string MethodName = nameof(ComparerBuilderInterception.InterceptGetHashCode);
        var comparerType = typeof(IEqualityComparer<TProperty>);
        return ApplyInterception(interception, MethodName, expression, obj, null, comparer, comparerType, sourceInfo, (a, b, c) => MakeGetHashCode(a, c));
      }//if
    }
  
    private static Expression InterceptCompare(ComparerBuilderInterception interception, Expression expression, Expression x, Expression y, Expression comparer, SourceInfo sourceInfo) {
      if(interception == null) {
        return expression;
      } else {
        const string MethodName = nameof(ComparerBuilderInterception.InterceptCompare);
        var comparerType = typeof(IComparer<TProperty>);
        return ApplyInterception(interception, MethodName, expression, x, y, comparer, comparerType, sourceInfo, (a, b, c) => MakeCompare(a, b, c));
      }//if
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
      return Block(expression.Type, variables, expressions);
    }
  
    #endregion Interception
  
    public override string ToString() => Expression.ToString();
  
    #region IComparerExpression Members
  
    public Expression AsEquals(ParameterExpression x, ParameterExpression y, ComparerBuilderInterception interception = null) {
      var first = ReplaceParameters(Expression, x);
      var second = ReplaceParameters(Expression, y);
      var expression = MakeEquals(first, second, EqualityComparer);
      return InterceptEquals(interception, expression, first, second, EqualityComparer, SourceInfo);
    }
  
    public Expression AsGetHashCode(ParameterExpression obj, ComparerBuilderInterception interception = null) {
      var value = ReplaceParameters(Expression, obj);
      var expression = MakeGetHashCode(value, EqualityComparer);
      return InterceptGetHashCode(interception, expression, value, EqualityComparer, SourceInfo);
    }
  
    public Expression AsCompare(ParameterExpression x, ParameterExpression y, ComparerBuilderInterception interception = null) {
      var first = ReplaceParameters(Expression, x);
      var second = ReplaceParameters(Expression, y);
      var expression = MakeCompare(first, second, Comparer);
      return InterceptCompare(interception, expression, first, second, Comparer, SourceInfo);
    }
  
    #endregion IComparerExpression Members
  }
}
