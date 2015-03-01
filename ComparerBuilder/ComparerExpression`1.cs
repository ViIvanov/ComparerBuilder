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

  internal sealed class ComparerExpression<T> : IComparerExpression, IComparerBuilderInterceptionArgs<T>
  {
    private static readonly MethodInfo GetHashCodeMethodInfo = typeof(T).GetMethod(nameof(GetHashCode), BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
    private static readonly Type[] InterceptTypeArguments = { typeof(T), };

    private static readonly ConstantExpression NullEqualityComparer = Constant(null, typeof(IEqualityComparer<T>));
    private static readonly ConstantExpression NullComparer = Constant(null, typeof(IComparer<T>));

    public ComparerExpression(LambdaExpression expression, IEqualityComparer<T> equality, IComparer<T> comparison, string filePath, int lineNumber) {
      if(expression == null) {
        throw new ArgumentNullException(nameof(expression));
      }//if

      Expression = expression;
      EqualityComparer = equality;
      Comparer = comparison;
      FilePath = filePath ?? String.Empty;
      LineNumber = lineNumber;

      EqualityComparerExpression = EqualityComparer != null ? Constant(EqualityComparer) : null;
      ComparisonComparerExpression = Comparer != null ? Constant(Comparer) : null;
    }

    private Expression EqualityComparerExpression { get; }
    private Expression ComparisonComparerExpression { get; }

    #region IComparerBuilderInterceptionArgs<T> Members

    public LambdaExpression Expression { get; }
    public IEqualityComparer<T> EqualityComparer { get; }
    public IComparer<T> Comparer { get; }
    public string FilePath { get; }
    public int LineNumber { get; }

    #endregion IComparerBuilderInterceptionArgs<T> Members

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

    private Expression ApplyInterception(IComparerBuilderInterception interception, string methodName,
      Expression first, Expression second, Func<Expression, Expression, Expression, Expression> make, bool comparison) {
      if(interception == null) {
        throw new ArgumentNullException(nameof(interception));
      } else if(first == null) {
        throw new ArgumentNullException(nameof(first));
      } else if(make == null) {
        throw new ArgumentNullException(nameof(make));
      }//if

      // return {interception}.{methodName}<{valueType}>({expression}, {x}[, {y}], this);

      var instance = Constant(interception);

      var useSecond = second != null;
      var firstArg = Parameter(first.Type);
      var assignFirst = Assign(firstArg, first);
      var secondArg = useSecond ? Parameter(second.Type) : null;
      var assignSecond = useSecond ? Assign(secondArg, second) : null;
      var variables = useSecond ? new[] { firstArg, secondArg, } : new[] { firstArg, };

      var comparer = !comparison ? EqualityComparerExpression : ComparisonComparerExpression;
      var valueArg = make(firstArg, secondArg, comparer);
      var args = Constant(this);
      var arguments = useSecond
        ? new[] { valueArg, firstArg, secondArg, args, }
        : new[] { valueArg, firstArg, args, };
      var call = Call(instance, methodName, InterceptTypeArguments, arguments);
      var expressions = useSecond
        ? new Expression[] { assignFirst, assignSecond, call, }
        : new Expression[] { assignFirst, call, };

      return Block(valueArg.Type, variables, expressions);
    }

    public override string ToString() => Expression.ToString();

    #region IComparerExpression Members

    public Expression AsEquals(ParameterExpression x, ParameterExpression y, IComparerBuilderInterception interception) {
      var first = ReplaceParameters(Expression, x);
      var second = ReplaceParameters(Expression, y);
      if(interception == null) {
        return MakeEquals(first, second, EqualityComparerExpression);
      } else {
        const string MethodName = nameof(IComparerBuilderInterception.InterceptEquals);
        var comparerType = typeof(IEqualityComparer<T>);
        return ApplyInterception(interception, MethodName, first, second, (a, b, c) => MakeEquals(a, b, c), comparison: false);
      }//if
    }

    public Expression AsGetHashCode(ParameterExpression obj, IComparerBuilderInterception interception) {
      var value = ReplaceParameters(Expression, obj);
      if(interception == null) {
        return MakeGetHashCode(value, EqualityComparerExpression);
      } else {
        const string MethodName = nameof(IComparerBuilderInterception.InterceptGetHashCode);
        var comparerType = typeof(IEqualityComparer<T>);
        return ApplyInterception(interception, MethodName, value, null, (a, b, c) => MakeGetHashCode(a, c), comparison: false);
      }//if
    }

    public Expression AsCompare(ParameterExpression x, ParameterExpression y, IComparerBuilderInterception interception) {
      var first = ReplaceParameters(Expression, x);
      var second = ReplaceParameters(Expression, y);
      if(interception == null) {
        return MakeCompare(first, second, ComparisonComparerExpression);
      } else {
        const string MethodName = nameof(IComparerBuilderInterception.InterceptCompare);
        var comparerType = typeof(IComparer<T>);
        return ApplyInterception(interception, MethodName, first, second, (a, b, c) => MakeCompare(a, b, c), comparison: true);
      }//if
    }

    #endregion IComparerExpression Members
  }
}
